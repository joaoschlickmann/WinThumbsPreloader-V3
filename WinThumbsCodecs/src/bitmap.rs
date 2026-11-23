use std::ptr;
use windows::Win32::Graphics::Gdi::{
    BI_RGB, BITMAPINFO, BITMAPINFOHEADER, CreateDIBSection, DIB_RGB_COLORS, HBITMAP,
};
use windows::Win32::UI::Shell::{WTS_ALPHATYPE, WTSAT_ARGB, WTSAT_RGB};

pub const MAX_THUMB_DIM: u32 = 2560;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PixelLayout {
    RgbaStraight,
    RgbaPremul,
    BgraPremul,
}

#[derive(Debug, Clone)]
pub struct ThumbBitmap {
    pub width: u32,
    pub height: u32,
    pub pixels: Vec<u8>,
    pub layout: PixelLayout,
    pub has_alpha: bool,
}

impl ThumbBitmap {
    pub fn from_rgba_straight(width: u32, height: u32, pixels: Vec<u8>, has_alpha: bool) -> Result<Self, String> {
        validate_dimensions(width, height, pixels.len())?;
        Ok(Self {
            width,
            height,
            pixels,
            layout: PixelLayout::RgbaStraight,
            has_alpha,
        })
    }

    pub fn from_rgba_premul(width: u32, height: u32, pixels: Vec<u8>, has_alpha: bool) -> Result<Self, String> {
        validate_dimensions(width, height, pixels.len())?;
        Ok(Self {
            width,
            height,
            pixels,
            layout: PixelLayout::RgbaPremul,
            has_alpha,
        })
    }

    pub fn from_bgra_premul(width: u32, height: u32, pixels: Vec<u8>, has_alpha: bool) -> Result<Self, String> {
        validate_dimensions(width, height, pixels.len())?;
        Ok(Self {
            width,
            height,
            pixels,
            layout: PixelLayout::BgraPremul,
            has_alpha,
        })
    }

    pub fn from_dynamic_image(img: image::DynamicImage) -> Result<Self, String> {
        let has_alpha = img.color().has_alpha();
        let rgba = img.to_rgba8();
        let (width, height) = rgba.dimensions();
        Self::from_rgba_straight(width, height, rgba.into_raw(), has_alpha)
    }
}

pub fn clamp_cx(cx: u32) -> u32 {
    cx.clamp(1, MAX_THUMB_DIM)
}

pub fn fit_dimensions(src_w: u32, src_h: u32, max_cx: u32) -> Result<(u32, u32), String> {
    if src_w == 0 || src_h == 0 {
        return Err("Image has zero width or height".to_string());
    }
    let target_max = clamp_cx(max_cx);
    let max_side = src_w.max(src_h);
    if max_side <= target_max {
        return Ok((src_w, src_h));
    }

    let scale = (target_max as f64) / (max_side as f64);
    let dst_w = ((src_w as f64) * scale).round().clamp(1.0, target_max as f64) as u32;
    let dst_h = ((src_h as f64) * scale).round().clamp(1.0, target_max as f64) as u32;
    Ok((dst_w, dst_h))
}

/// Resizes `bitmap` to fit within `max_cx` without upscaling images already smaller than `max_cx`.
pub fn resize_fit(bitmap: ThumbBitmap, max_cx: u32) -> Result<ThumbBitmap, String> {
    validate_dimensions(bitmap.width, bitmap.height, bitmap.pixels.len())?;
    let target_max = clamp_cx(max_cx);
    let (dst_w, dst_h) = fit_dimensions(bitmap.width, bitmap.height, target_max)?;
    if dst_w == bitmap.width && dst_h == bitmap.height {
        return Ok(bitmap);
    }

    let rgba_pixels = match bitmap.layout {
        PixelLayout::RgbaStraight | PixelLayout::RgbaPremul => bitmap.pixels,
        PixelLayout::BgraPremul => {
            let mut swapped = bitmap.pixels;
            for px in swapped.chunks_exact_mut(4) {
                px.swap(0, 2);
            }
            swapped
        }
    };

    let src_img = image::RgbaImage::from_raw(bitmap.width, bitmap.height, rgba_pixels)
        .ok_or_else(|| "Invalid RGBA buffer dimensions".to_string())?;
    let resized = image::imageops::resize(
        &src_img,
        dst_w,
        dst_h,
        image::imageops::FilterType::Triangle,
    );

    let out_layout = if bitmap.layout == PixelLayout::RgbaStraight {
        PixelLayout::RgbaStraight
    } else {
        PixelLayout::RgbaPremul
    };

    Ok(ThumbBitmap {
        width: dst_w,
        height: dst_h,
        pixels: resized.into_raw(),
        layout: out_layout,
        has_alpha: bitmap.has_alpha,
    })
}

pub fn to_hbitmap(bitmap: &ThumbBitmap) -> Result<(HBITMAP, WTS_ALPHATYPE), String> {
    if bitmap.width == 0 || bitmap.height == 0 {
        return Err("Cannot allocate zero-sized HBITMAP".to_string());
    }
    if bitmap.width > MAX_THUMB_DIM || bitmap.height > MAX_THUMB_DIM {
        return Err("Bitmap dimensions exceed maximum thumbnail limit".to_string());
    }
    validate_dimensions(bitmap.width, bitmap.height, bitmap.pixels.len())?;

    let mut bmi = BITMAPINFO::default();
    bmi.bmiHeader = BITMAPINFOHEADER {
        biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
        biWidth: bitmap.width as i32,
        biHeight: -(bitmap.height as i32), // Top-down DIB
        biPlanes: 1,
        biBitCount: 32,
        biCompression: BI_RGB.0,
        biSizeImage: 0,
        biXPelsPerMeter: 0,
        biYPelsPerMeter: 0,
        biClrUsed: 0,
        biClrImportant: 0,
    };

    let mut bits_ptr: *mut core::ffi::c_void = ptr::null_mut();
    let hbmp = unsafe {
        CreateDIBSection(None, &bmi, DIB_RGB_COLORS, &mut bits_ptr, None, 0)
            .map_err(|e| format!("CreateDIBSection failed: {e}"))?
    };

    if hbmp.is_invalid() || bits_ptr.is_null() {
        return Err("CreateDIBSection returned null".to_string());
    }

    let pixel_count = (bitmap.width as usize)
        .checked_mul(bitmap.height as usize)
        .ok_or_else(|| "Pixel count overflow".to_string())?;
    let byte_len = pixel_count
        .checked_mul(4)
        .ok_or_else(|| "Byte length overflow".to_string())?;

    // Safety: CreateDIBSection guarantees `bits_ptr` points to `width * height * 4` bytes.
    let dst = unsafe { std::slice::from_raw_parts_mut(bits_ptr.cast::<u8>(), byte_len) };

    match bitmap.layout {
        PixelLayout::RgbaStraight => {
            if bitmap.has_alpha {
                for (src_px, dst_px) in bitmap.pixels.chunks_exact(4).zip(dst.chunks_exact_mut(4)) {
                    let a = src_px[3] as u16;
                    dst_px[0] = ((src_px[2] as u16 * a + 127) / 255) as u8;
                    dst_px[1] = ((src_px[1] as u16 * a + 127) / 255) as u8;
                    dst_px[2] = ((src_px[0] as u16 * a + 127) / 255) as u8;
                    dst_px[3] = a as u8;
                }
            } else {
                for (src_px, dst_px) in bitmap.pixels.chunks_exact(4).zip(dst.chunks_exact_mut(4)) {
                    dst_px[0] = src_px[2];
                    dst_px[1] = src_px[1];
                    dst_px[2] = src_px[0];
                    dst_px[3] = 255;
                }
            }
        }
        PixelLayout::RgbaPremul => {
            if bitmap.has_alpha {
                for (src_px, dst_px) in bitmap.pixels.chunks_exact(4).zip(dst.chunks_exact_mut(4)) {
                    dst_px[0] = src_px[2];
                    dst_px[1] = src_px[1];
                    dst_px[2] = src_px[0];
                    dst_px[3] = src_px[3];
                }
            } else {
                for (src_px, dst_px) in bitmap.pixels.chunks_exact(4).zip(dst.chunks_exact_mut(4)) {
                    dst_px[0] = src_px[2];
                    dst_px[1] = src_px[1];
                    dst_px[2] = src_px[0];
                    dst_px[3] = 255;
                }
            }
        }
        PixelLayout::BgraPremul => {
            dst.copy_from_slice(&bitmap.pixels);
            if !bitmap.has_alpha {
                for dst_px in dst.chunks_exact_mut(4) {
                    dst_px[3] = 255;
                }
            }
        }
    }

    let alpha_type = if bitmap.has_alpha { WTSAT_ARGB } else { WTSAT_RGB };
    Ok((hbmp, alpha_type))
}

fn validate_dimensions(width: u32, height: u32, len: usize) -> Result<(), String> {
    if width == 0 || height == 0 {
        return Err("Zero image dimensions".to_string());
    }
    let expected = (width as usize)
        .checked_mul(height as usize)
        .and_then(|p| p.checked_mul(4))
        .ok_or_else(|| "Image byte size overflow".to_string())?;
    if len != expected {
        return Err(format!("Pixel buffer length mismatch: expected {expected}, got {len}"));
    }
    Ok(())
}
