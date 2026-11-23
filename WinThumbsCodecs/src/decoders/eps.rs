use std::ptr;
use windows::Win32::Graphics::Gdi::{
    BI_RGB, BITMAPINFO, BITMAPINFOHEADER, CreateCompatibleDC, CreateDIBSection, DIB_RGB_COLORS,
    DeleteDC, DeleteMetaFile, DeleteObject, HBITMAP, HDC, HGDIOBJ, HMETAFILE, MM_ANISOTROPIC,
    PatBlt, PlayMetaFile, SelectObject, SetMapMode, SetMetaFileBitsEx, SetViewportExtEx,
    SetWindowExtEx, SetWindowOrgEx, WHITENESS,
};
use windows::Win32::System::Com::IStream;

use crate::bitmap::{ThumbBitmap, clamp_cx, fit_dimensions};
use crate::stream::{DEFAULT_MAX_STREAM_BYTES, StreamReader};

const ALDUS_WMF_MAGIC: u32 = 0x9AC6CDD7;
const ALDUS_HEADER_LEN: usize = 22;

pub fn decode_stream(stream: &IStream, cx: u32) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let bytes = reader.read_all_capped(DEFAULT_MAX_STREAM_BYTES)?;
    decode_bytes(&bytes, cx)
}

pub fn decode_bytes(bytes: &[u8], cx: u32) -> Result<ThumbBitmap, String> {
    let target_cx = clamp_cx(cx);

    if let Ok(sec) = craft_eps::sections(bytes) {
        if let Some(tiff_bytes) = sec.tiff
            && let Ok(img) = image::load_from_memory_with_format(tiff_bytes, image::ImageFormat::Tiff)
        {
            return ThumbBitmap::from_dynamic_image(img);
        }

        if let Some(wmf_bytes) = sec.metafile
            && let Ok(bmp) = render_wmf(wmf_bytes, target_cx)
        {
            return Ok(bmp);
        }

        if let Some(epsi) = craft_eps::epsi(sec.postscript)
            && let Ok(bmp) = epsi_to_bitmap(epsi)
        {
            return Ok(bmp);
        }
    } else if let Some(epsi) = craft_eps::epsi(bytes) {
        return epsi_to_bitmap(epsi);
    }

    let limits = craft_ps::read::Limits {
        steps: 15_000_000,
        memory: 64 * 1024 * 1024,
        ..Default::default()
    };
    let converted = craft_ps::read::eps_to_pdf(bytes, limits)
        .map_err(|e| format!("EPS PostScript conversion failed: {e}"))?;
    super::pdf_ai::render_pdf_bytes(&converted.pdf, target_cx)
}

fn epsi_to_bitmap(epsi: craft_eps::Bitmap) -> Result<ThumbBitmap, String> {
    let expected = (epsi.width as usize)
        .checked_mul(epsi.height as usize)
        .ok_or_else(|| "EPSI dimensions overflow".to_string())?;
    if epsi.gray.len() < expected {
        return Err("Truncated EPSI bitmap".to_string());
    }

    let mut rgba = Vec::with_capacity(expected * 4);
    for &g in &epsi.gray[..expected] {
        rgba.extend_from_slice(&[g, g, g, 255]);
    }
    ThumbBitmap::from_rgba_straight(epsi.width, epsi.height, rgba, false)
}

struct ScopedDc(HDC);
impl Drop for ScopedDc {
    fn drop(&mut self) {
        if !self.0.is_invalid() {
            unsafe { let _ = DeleteDC(self.0); }
        }
    }
}

struct ScopedBitmapHandle(HBITMAP);
impl Drop for ScopedBitmapHandle {
    fn drop(&mut self) {
        if !self.0.is_invalid() {
            unsafe { let _ = DeleteObject(HGDIOBJ(self.0.0)); }
        }
    }
}

struct ScopedMetafile(HMETAFILE);
impl Drop for ScopedMetafile {
    fn drop(&mut self) {
        if !self.0.is_invalid() {
            unsafe { let _ = DeleteMetaFile(self.0); }
        }
    }
}

fn render_wmf(raw: &[u8], max_cx: u32) -> Result<ThumbBitmap, String> {
    let (wmf_body, origin_x, origin_y, src_w, src_h) = parse_wmf_bounds(raw)?;
    let (dst_w, dst_h) = fit_dimensions(src_w, src_h, max_cx)?;

    let hmf = unsafe { SetMetaFileBitsEx(wmf_body) };
    if hmf.is_invalid() {
        return Err("SetMetaFileBitsEx failed".to_string());
    }
    let hmf_guard = ScopedMetafile(hmf);

    let hdc = unsafe { CreateCompatibleDC(None) };
    if hdc.is_invalid() {
        return Err("CreateCompatibleDC failed".to_string());
    }
    let dc_guard = ScopedDc(hdc);

    let mut bmi = BITMAPINFO::default();
    bmi.bmiHeader = BITMAPINFOHEADER {
        biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
        biWidth: dst_w as i32,
        biHeight: -(dst_h as i32),
        biPlanes: 1,
        biBitCount: 32,
        biCompression: BI_RGB.0,
        ..Default::default()
    };

    let mut bits_ptr: *mut core::ffi::c_void = ptr::null_mut();
    let hbmp = unsafe {
        CreateDIBSection(Some(dc_guard.0), &bmi, DIB_RGB_COLORS, &mut bits_ptr, None, 0)
            .map_err(|e| format!("CreateDIBSection for WMF failed: {e}"))?
    };
    if hbmp.is_invalid() || bits_ptr.is_null() {
        return Err("CreateDIBSection for WMF returned null".to_string());
    }
    let bmp_guard = ScopedBitmapHandle(hbmp);

    let old_obj = unsafe { SelectObject(dc_guard.0, HGDIOBJ(bmp_guard.0.0)) };
    unsafe {
        let _ = PatBlt(dc_guard.0, 0, 0, dst_w as i32, dst_h as i32, WHITENESS);
        let _ = SetMapMode(dc_guard.0, MM_ANISOTROPIC);
        let _ = SetWindowOrgEx(dc_guard.0, origin_x, origin_y, None);
        let _ = SetWindowExtEx(dc_guard.0, src_w as i32, src_h as i32, None);
        let _ = SetViewportExtEx(dc_guard.0, dst_w as i32, dst_h as i32, None);
        let ok = PlayMetaFile(dc_guard.0, hmf_guard.0);
        let _ = SelectObject(dc_guard.0, old_obj);
        if !ok.as_bool() {
            return Err("PlayMetaFile failed".to_string());
        }
    }

    let byte_len = (dst_w as usize) * (dst_h as usize) * 4;
    let src_slice = unsafe { std::slice::from_raw_parts(bits_ptr.cast::<u8>(), byte_len) };
    let mut bgra = src_slice.to_vec();
    for px in bgra.chunks_exact_mut(4) {
        px[3] = 255;
    }

    ThumbBitmap::from_bgra_premul(dst_w, dst_h, bgra, false)
}

fn parse_wmf_bounds(raw: &[u8]) -> Result<(&[u8], i32, i32, u32, u32), String> {
    if raw.len() >= ALDUS_HEADER_LEN {
        let key = u32::from_le_bytes([raw[0], raw[1], raw[2], raw[3]]);
        if key == ALDUS_WMF_MAGIC {
            let left = i16::from_le_bytes([raw[6], raw[7]]) as i32;
            let top = i16::from_le_bytes([raw[8], raw[9]]) as i32;
            let right = i16::from_le_bytes([raw[10], raw[11]]) as i32;
            let bottom = i16::from_le_bytes([raw[12], raw[13]]) as i32;
            let w = (right - left).unsigned_abs().max(1);
            let h = (bottom - top).unsigned_abs().max(1);
            return Ok((&raw[ALDUS_HEADER_LEN..], left, top, w, h));
        }
    }

    if raw.len() < 18 {
        return Err("WMF too short".to_string());
    }
    Ok((raw, 0, 0, 512, 512))
}
