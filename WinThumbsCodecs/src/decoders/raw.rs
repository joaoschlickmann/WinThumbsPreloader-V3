use windows::Win32::System::Com::IStream;

use crate::bitmap::{ThumbBitmap, clamp_cx};
use crate::stream::{DEFAULT_MAX_STREAM_BYTES, StreamReader};

const MAX_BOUNDED_RAW_WINDOW: usize = 32 * 1024 * 1024;

pub fn decode_stream(stream: &IStream, cx: u32) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let target_cx = clamp_cx(cx);

    if reader.total_size() > DEFAULT_MAX_STREAM_BYTES as u64 {
        let prefix = reader.read_prefix(256 * 1024)?;
        let orient = craft_raw::orientation(&prefix).unwrap_or(1);
        let range = craft_raw::bounded_preview(&prefix)
            .ok_or_else(|| "Oversized RAW has no bounded preview".to_string())?;
        let win_len = usize::try_from(range.window_len)
            .ok()
            .filter(|&l| l <= MAX_BOUNDED_RAW_WINDOW)
            .ok_or_else(|| "Oversized RAW preview window exceeds 32 MiB limit".to_string())?;
        let window = reader.read_range(range.offset, win_len)?;
        let preview_bytes = craft_raw::embedded_preview_from_range(&window, range)
            .ok_or_else(|| "Failed to extract bounded RAW preview".to_string())?;
        return decode_preview_payload(&preview_bytes, None, orient);
    }

    let bytes = reader.read_all_capped(DEFAULT_MAX_STREAM_BYTES)?;
    decode_bytes(&bytes, target_cx)
}

pub fn decode_bytes(bytes: &[u8], cx: u32) -> Result<ThumbBitmap, String> {
    let target_cx = clamp_cx(cx);
    let orient = craft_raw::orientation(bytes).unwrap_or(1);
    let preview_bytes = craft_raw::embedded_preview_covering(bytes, target_cx)
        .or_else(|| craft_raw::embedded_preview(bytes))
        .ok_or_else(|| "No embedded preview found in RAW file".to_string())?;
    decode_preview_payload(&preview_bytes, Some(bytes), orient)
}

fn decode_preview_payload(
    preview_bytes: &[u8],
    full_file: Option<&[u8]>,
    exif_orientation: u16,
) -> Result<ThumbBitmap, String> {
    if preview_bytes.starts_with(&[0xFF, 0xD8]) {
        let img = image::load_from_memory_with_format(preview_bytes, image::ImageFormat::Jpeg)
            .map_err(|e| format!("Failed decoding RAW JPEG preview: {e}"))?;
        return ThumbBitmap::from_dynamic_image(apply_exif_orientation(img, exif_orientation));
    }

    if preview_bytes.starts_with(b"II*\0") || preview_bytes.starts_with(b"MM\0*") {
        let img = image::load_from_memory_with_format(preview_bytes, image::ImageFormat::Tiff)
            .map_err(|e| format!("Failed decoding RAW TIFF preview: {e}"))?;
        return ThumbBitmap::from_dynamic_image(apply_exif_orientation(img, exif_orientation));
    }

    if is_jxl(preview_bytes)
        && let Some(raw_bytes) = full_file
        && let Some(decoded) = craft_raw::embedded_jxl_preview(raw_bytes, &craft_raw::Limits::default())
    {
        let mut rgba = Vec::with_capacity((decoded.width as usize) * (decoded.height as usize) * 4);
        let shift = decoded.bits.saturating_sub(8).min(16);
        for chunk in decoded.rgb.chunks_exact(3) {
            let r = ((chunk[0] >> shift) & 0xFF) as u8;
            let g = ((chunk[1] >> shift) & 0xFF) as u8;
            let b = ((chunk[2] >> shift) & 0xFF) as u8;
            rgba.extend_from_slice(&[r, g, b, 255]);
        }
        let buf = image::RgbaImage::from_raw(decoded.width, decoded.height, rgba)
            .ok_or_else(|| "Invalid JXL preview dimensions".to_string())?;
        let oriented = apply_exif_orientation(image::DynamicImage::ImageRgba8(buf), exif_orientation);
        return ThumbBitmap::from_dynamic_image(oriented);
    }

    // Generic fallback if a container embeds PNG/BMP.
    let img = image::load_from_memory(preview_bytes)
        .map_err(|e| format!("Unsupported RAW preview payload: {e}"))?;
    ThumbBitmap::from_dynamic_image(apply_exif_orientation(img, exif_orientation))
}

fn is_jxl(bytes: &[u8]) -> bool {
    bytes.starts_with(&[0xFF, 0x0A]) || bytes.starts_with(b"\x00\x00\x00\x0CJXL ")
}

fn apply_exif_orientation(img: image::DynamicImage, orientation: u16) -> image::DynamicImage {
    match orientation {
        2 => img.fliph(),
        3 => img.rotate180(),
        4 => img.flipv(),
        5 => img.rotate90().fliph(),
        6 => img.rotate90(),
        7 => img.rotate270().fliph(),
        8 => img.rotate270(),
        _ => img,
    }
}
