use std::sync::Arc;
use windows::Win32::System::Com::IStream;

use craft_cms::{Builtin, ColorSpace, Profile, TransformOptions};
use craft_psd::{ColorMode, Compression, Head, ImageResource, PsdFile, Source, merged::Merged, resources};

use crate::bitmap::{ThumbBitmap, clamp_cx};
use crate::stream::{DEFAULT_MAX_STREAM_BYTES, StreamReader};

const LARGE_PSB_PREFIX_BYTES: usize = 16 * 1024 * 1024;
const MAX_COMPOSITE_PIXELS: u64 = 64_000_000;

pub fn decode_stream(stream: &IStream, cx: u32) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let target_cx = clamp_cx(cx);

    if reader.total_size() > DEFAULT_MAX_STREAM_BYTES as u64 {
        let prefix = reader.read_prefix(LARGE_PSB_PREFIX_BYTES)?;
        let head = Head::from_bytes(&prefix)
            .map_err(|e| format!("Failed to parse oversized PSB header: {e}"))?;
        return extract_embedded_thumb(&head.resources)
            .ok_or_else(|| "Oversized PSB has no embedded thumbnail resource".to_string());
    }

    let bytes = reader.read_all_capped(DEFAULT_MAX_STREAM_BYTES)?;
    decode_bytes(&bytes, target_cx)
}

pub fn decode_bytes(bytes: &[u8], cx: u32) -> Result<ThumbBitmap, String> {
    let target_cx = clamp_cx(cx);
    let head = Head::from_bytes(bytes).map_err(|e| format!("PSD/PSB header parse failed: {e}"))?;
    let embedded = extract_embedded_thumb(&head.resources);

    if let Some(ref thumb) = embedded
        && thumb.width.max(thumb.height) >= target_cx
    {
        return Ok(thumb.clone());
    }

    let arc_bytes = Arc::new(bytes.to_vec());
    if let Ok(psd) = PsdFile::from_source(Source::from_arc(arc_bytes.clone()))
        && psd.has_real_merged_data() != Some(false)
        && let Some(bmp) = decode_merged_composite(&psd, &arc_bytes, target_cx)
    {
        return Ok(bmp);
    }

    embedded.ok_or_else(|| "No usable merged image or embedded thumbnail in PSD/PSB".to_string())
}

fn decode_merged_composite(psd: &PsdFile, bytes: &[u8], cx: u32) -> Option<ThumbBitmap> {
    let h = &psd.header;
    if h.width == 0 || h.height == 0 {
        return None;
    }

    if matches!(h.color_mode, ColorMode::Rgb | ColorMode::Grayscale)
        && matches!(psd.image_data.compression, Compression::Raw | Compression::Rle)
        && let Ok(Some(preview)) = psd.merged_preview(cx)
    {
        let mut rgba = preview.rgba;
        if h.color_mode == ColorMode::Rgb
            && let Some(icc) = psd.icc_profile()
            && let Ok(src_prof) = Profile::parse(icc)
            && src_prof.color_space == ColorSpace::Rgb
            && let Ok(transform) = craft_cms::cached(&src_prof, craft_cms::builtin::srgb(), TransformOptions::default())
        {
            let mut converted = vec![0u8; rgba.len()];
            if transform.convert_u8(&rgba, 4, &mut converted, 4, true).is_ok() {
                rgba = converted;
            }
        }
        return ThumbBitmap::from_rgba_straight(preview.width, preview.height, rgba, false).ok();
    }

    let pixel_count = u64::from(h.width).checked_mul(u64::from(h.height))?;
    if pixel_count > MAX_COMPOSITE_PIXELS {
        return None;
    }

    if h.color_mode == ColorMode::Cmyk
        && let Some(cmyk_bmp) = decode_cmyk_merged(psd, bytes, cx)
    {
        return Some(cmyk_bmp);
    }

    let rgba = psd.composite_rgba8().ok()?;
    let has_alpha = psd.merged_has_alpha();
    ThumbBitmap::from_rgba_straight(rgba.width, rgba.height, rgba.data, has_alpha).ok()
}

fn decode_cmyk_merged(psd: &PsdFile, bytes: &[u8], cx: u32) -> Option<ThumbBitmap> {
    let merged = Merged::open(bytes, MAX_COMPOSITE_PIXELS).ok()?;
    if !matches!(merged.compression, 0 | 1) {
        return None;
    }

    let h = &merged.head.header;
    if !matches!(h.depth, 8 | 16 | 32) || h.channels < 4 {
        return None;
    }

    let has_alpha = merged.transparency && h.channels >= 5;
    let used = if has_alpha { 5 } else { 4 };
    let planes = merged.read_planes(used).ok()?;
    if planes.len() < 4 {
        return None;
    }

    let w = h.width;
    let height = h.height;
    let k = w.max(height).div_ceil((cx * 2).max(1)).max(1);
    let out_w = w.div_ceil(k) as usize;
    let out_h = height.div_ceil(k) as usize;
    let out_pixels = out_w.checked_mul(out_h)?;
    let bps = usize::from(h.depth / 8);

    let mut cmyk_src = vec![0u8; out_pixels.checked_mul(4)?];
    let mut alphas = if has_alpha { Some(vec![255u8; out_pixels]) } else { None };

    for oy in 0..out_h {
        let sy = oy * (k as usize);
        for ox in 0..out_w {
            let sx = ox * (k as usize);
            let src_idx = (sy * (w as usize) + sx) * bps;
            let dst_idx = (oy * out_w + ox) * 4;
            for ch in 0..4 {
                let sample = read_sample_u8(&planes[ch], src_idx, h.depth);
                // PSD stores CMYK inverted (255 = 0% ink); ICC profiles expect 0 = 0% ink.
                cmyk_src[dst_idx + ch] = 255u8.saturating_sub(sample);
            }
            if let Some(ref mut a_buf) = alphas {
                a_buf[oy * out_w + ox] = read_sample_u8(&planes[4], src_idx, h.depth);
            }
        }
    }

    let embedded_profile = psd
        .icc_profile()
        .and_then(|icc| Profile::parse(icc).ok())
        .filter(|p| p.color_space == ColorSpace::Cmyk);
    let src_profile = embedded_profile
        .as_ref()
        .unwrap_or_else(|| Builtin::CoatedFogra39.profile());

    let transform = craft_cms::cached(src_profile, craft_cms::builtin::srgb(), TransformOptions::default()).ok()?;
    let mut rgba = vec![255u8; out_pixels.checked_mul(4)?];
    transform.convert_u8(&cmyk_src, 4, &mut rgba, 4, false).ok()?;

    if let Some(a_buf) = alphas {
        for (px, a) in rgba.chunks_exact_mut(4).zip(a_buf) {
            px[3] = a;
        }
    }

    ThumbBitmap::from_rgba_straight(out_w as u32, out_h as u32, rgba, has_alpha).ok()
}

fn read_sample_u8(plane: &[u8], offset: usize, depth: u16) -> u8 {
    match depth {
        8 => plane.get(offset).copied().unwrap_or(0),
        16 => plane
            .get(offset..offset + 2)
            .map(|b| ((u32::from(u16::from_be_bytes([b[0], b[1]])) * 255 + 32767) / 65535) as u8)
            .unwrap_or(0),
        32 => plane
            .get(offset..offset + 4)
            .map(|b| {
                let v = f32::from_be_bytes([b[0], b[1], b[2], b[3]]);
                if v.is_nan() {
                    0
                } else {
                    (v.clamp(0.0, 1.0) * 255.0).round() as u8
                }
            })
            .unwrap_or(0),
        _ => 0,
    }
}

fn extract_embedded_thumb(res_list: &[ImageResource]) -> Option<ThumbBitmap> {
    for target_id in [resources::ids::THUMBNAIL, resources::ids::THUMBNAIL_PS4] {
        let Some(res) = res_list.iter().find(|r| r.id == target_id) else {
            continue;
        };
        let Ok(thumb) = resources::Thumbnail::from_bytes(&res.data) else {
            continue;
        };
        let is_bgr = target_id == resources::ids::THUMBNAIL_PS4;

        if thumb.format == 1 {
            if let Ok(dyn_img) = image::load_from_memory_with_format(thumb.data, image::ImageFormat::Jpeg) {
                let mut rgba = dyn_img.to_rgba8();
                if is_bgr {
                    for px in rgba.pixels_mut() {
                        px.0.swap(0, 2);
                    }
                }
                let (w, h) = rgba.dimensions();
                if let Ok(bmp) = ThumbBitmap::from_rgba_straight(w, h, rgba.into_raw(), false) {
                    return Some(bmp);
                }
            }
        } else if thumb.format == 0
            && thumb.width > 0
            && thumb.height > 0
            && thumb.width <= 4096
            && thumb.height <= 4096
            && thumb.bits_per_pixel == 24
            && thumb.planes == 1
        {
            let w = thumb.width as usize;
            let h = thumb.height as usize;
            let stride = ((w * 3) + 3) & !3;
            if let Some(needed) = stride.checked_mul(h)
                && thumb.data.len() >= needed
            {
                let mut rgba = Vec::with_capacity(w * h * 4);
                for y in 0..h {
                    let row = &thumb.data[y * stride..y * stride + w * 3];
                    for rgb in row.chunks_exact(3) {
                        if is_bgr {
                            rgba.extend_from_slice(&[rgb[2], rgb[1], rgb[0], 255]);
                        } else {
                            rgba.extend_from_slice(&[rgb[0], rgb[1], rgb[2], 255]);
                        }
                    }
                }
                if let Ok(bmp) = ThumbBitmap::from_rgba_straight(thumb.width, thumb.height, rgba, false) {
                    return Some(bmp);
                }
            }
        }
    }
    None
}
