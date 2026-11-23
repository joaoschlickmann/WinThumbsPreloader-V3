use core::ffi::{c_char, c_int, c_uint, c_void};
use std::ptr;
use std::sync::{Mutex, OnceLock};

use windows::Win32::Foundation::HMODULE;
use windows::Win32::System::Com::IStream;
use windows::Win32::System::LibraryLoader::{
    GetProcAddress, LOAD_LIBRARY_SEARCH_DEFAULT_DIRS, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR,
    LoadLibraryExW,
};
use windows::core::{HSTRING, PCSTR};

use crate::bitmap::{ThumbBitmap, fit_dimensions};
use crate::dll_directory;
use crate::stream::{DEFAULT_MAX_STREAM_BYTES, StreamReader};

type FpdfDocument = *mut c_void;
type FpdfPage = *mut c_void;
type FpdfBitmap = *mut c_void;

const FPDF_ANNOT: c_int = 0x01;
const FPDF_PRINTING: c_int = 0x800;
const FPDF_BITMAP_BGRA: c_int = 4;

#[repr(C)]
struct FpdfLibraryConfig {
    version: c_int,
    user_font_paths: *const *const c_char,
    isolate: *mut c_void,
    v8_embedder_slot: c_uint,
}

struct PdfiumApi {
    _module: isize,
    init_with_config: unsafe extern "C" fn(*const FpdfLibraryConfig),
    load_mem_document: unsafe extern "C" fn(*const c_void, c_int, *const c_char) -> FpdfDocument,
    close_document: unsafe extern "C" fn(FpdfDocument),
    get_page_count: unsafe extern "C" fn(FpdfDocument) -> c_int,
    load_page: unsafe extern "C" fn(FpdfDocument, c_int) -> FpdfPage,
    close_page: unsafe extern "C" fn(FpdfPage),
    get_page_width_f: unsafe extern "C" fn(FpdfPage) -> f32,
    get_page_height_f: unsafe extern "C" fn(FpdfPage) -> f32,
    bitmap_create_ex:
        unsafe extern "C" fn(c_int, c_int, c_int, *mut c_void, c_int) -> FpdfBitmap,
    bitmap_fill_rect: unsafe extern "C" fn(FpdfBitmap, c_int, c_int, c_int, c_int, u32) -> c_int,
    render_page_bitmap:
        unsafe extern "C" fn(FpdfBitmap, FpdfPage, c_int, c_int, c_int, c_int, c_int, c_int),
    bitmap_destroy: unsafe extern "C" fn(FpdfBitmap),
}

unsafe impl Send for PdfiumApi {}
unsafe impl Sync for PdfiumApi {}

static PDFIUM: OnceLock<Result<PdfiumApi, String>> = OnceLock::new();
static PDFIUM_LOCK: Mutex<()> = Mutex::new(());

unsafe fn load_sym<T: Copy>(module: HMODULE, name: &core::ffi::CStr) -> Result<T, String> {
    let proc = unsafe { GetProcAddress(module, PCSTR(name.as_ptr().cast())) }
        .ok_or_else(|| format!("Missing PDFium symbol: {}", name.to_string_lossy()))?;
    // Safety: caller matches exact C ABI signature from fpdfview.h.
    Ok(unsafe { std::mem::transmute_copy(&proc) })
}

fn load_pdfium() -> Result<&'static PdfiumApi, String> {
    let res = PDFIUM.get_or_init(|| {
        let dir = dll_directory()
            .ok_or_else(|| "Unable to resolve codecs directory for pdfium.dll".to_string())?;
        let dll_path = dir.join("pdfium.dll");
        if !dll_path.is_file() {
            return Err(format!("pdfium.dll not found at {}", dll_path.display()));
        }

        let wide_path = HSTRING::from(dll_path.as_os_str());
        let module = unsafe {
            LoadLibraryExW(
                &wide_path,
                None,
                LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS,
            )
            .map_err(|e| format!("Failed to load pdfium.dll: {e}"))?
        };

        unsafe {
            let api = PdfiumApi {
                _module: module.0 as isize,
                init_with_config: load_sym(module, c"FPDF_InitLibraryWithConfig")?,
                load_mem_document: load_sym(module, c"FPDF_LoadMemDocument")?,
                close_document: load_sym(module, c"FPDF_CloseDocument")?,
                get_page_count: load_sym(module, c"FPDF_GetPageCount")?,
                load_page: load_sym(module, c"FPDF_LoadPage")?,
                close_page: load_sym(module, c"FPDF_ClosePage")?,
                get_page_width_f: load_sym(module, c"FPDF_GetPageWidthF")?,
                get_page_height_f: load_sym(module, c"FPDF_GetPageHeightF")?,
                bitmap_create_ex: load_sym(module, c"FPDFBitmap_CreateEx")?,
                bitmap_fill_rect: load_sym(module, c"FPDFBitmap_FillRect")?,
                render_page_bitmap: load_sym(module, c"FPDF_RenderPageBitmap")?,
                bitmap_destroy: load_sym(module, c"FPDFBitmap_Destroy")?,
            };

            let config = FpdfLibraryConfig {
                version: 2,
                user_font_paths: ptr::null(),
                isolate: ptr::null_mut(),
                v8_embedder_slot: 0,
            };
            (api.init_with_config)(&config);
            Ok(api)
        }
    });

    match res {
        Ok(api) => Ok(api),
        Err(e) => Err(e.clone()),
    }
}

struct ScopedDoc<'a> {
    api: &'a PdfiumApi,
    doc: FpdfDocument,
}
impl Drop for ScopedDoc<'_> {
    fn drop(&mut self) {
        if !self.doc.is_null() {
            unsafe { (self.api.close_document)(self.doc) };
        }
    }
}

struct ScopedPage<'a> {
    api: &'a PdfiumApi,
    page: FpdfPage,
}
impl Drop for ScopedPage<'_> {
    fn drop(&mut self) {
        if !self.page.is_null() {
            unsafe { (self.api.close_page)(self.page) };
        }
    }
}

struct ScopedBitmap<'a> {
    api: &'a PdfiumApi,
    bmp: FpdfBitmap,
}
impl Drop for ScopedBitmap<'_> {
    fn drop(&mut self) {
        if !self.bmp.is_null() {
            unsafe { (self.api.bitmap_destroy)(self.bmp) };
        }
    }
}

pub fn decode_pdf_stream(stream: &IStream, cx: u32) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let bytes = reader.read_all_capped(DEFAULT_MAX_STREAM_BYTES)?;
    render_pdf_bytes(&bytes, cx)
}

pub fn decode_ai_stream(stream: &IStream, cx: u32) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let bytes = reader.read_all_capped(DEFAULT_MAX_STREAM_BYTES)?;
    decode_ai_bytes(&bytes, cx)
}

pub fn decode_ai_bytes(bytes: &[u8], cx: u32) -> Result<ThumbBitmap, String> {
    match craft_eps::sniff(bytes) {
        Some(s) if s.kind == craft_eps::Kind::Pdf => render_pdf_bytes(bytes, cx),
        Some(s) if matches!(s.kind, craft_eps::Kind::Eps | craft_eps::Kind::PostScript) => {
            super::eps::decode_bytes(bytes, cx)
        }
        _ => {
            if bytes.starts_with(b"%PDF-") {
                render_pdf_bytes(bytes, cx)
            } else {
                Err("Unrecognized Adobe Illustrator file format".to_string())
            }
        }
    }
}

pub fn render_pdf_bytes(bytes: &[u8], cx: u32) -> Result<ThumbBitmap, String> {
    if bytes.is_empty() {
        return Err("Empty PDF buffer".to_string());
    }
    let size_i32 =
        c_int::try_from(bytes.len()).map_err(|_| "PDF exceeds i32 size limit".to_string())?;
    let api = load_pdfium()?;
    let _guard = PDFIUM_LOCK.lock().unwrap_or_else(|e| e.into_inner());

    let doc = unsafe { (api.load_mem_document)(bytes.as_ptr().cast(), size_i32, ptr::null()) };
    if doc.is_null() {
        return Err("FPDF_LoadMemDocument failed".to_string());
    }
    let doc_guard = ScopedDoc { api, doc };

    let page_count = unsafe { (api.get_page_count)(doc_guard.doc) };
    if page_count <= 0 {
        return Err("PDF contains no pages".to_string());
    }

    let page = unsafe { (api.load_page)(doc_guard.doc, 0) };
    if page.is_null() {
        return Err("FPDF_LoadPage(0) failed".to_string());
    }
    let page_guard = ScopedPage { api, page };

    let raw_w = unsafe { (api.get_page_width_f)(page_guard.page) };
    let raw_h = unsafe { (api.get_page_height_f)(page_guard.page) };
    if !raw_w.is_finite() || !raw_h.is_finite() || raw_w <= 0.0 || raw_h <= 0.0 {
        return Err("Invalid PDF page dimensions".to_string());
    }

    // Scale page points up cleanly before fitting so small point pages render sharply at `cx`.
    let max_pt = raw_w.max(raw_h);
    let scale = (cx.max(1) as f32) / max_pt;
    let src_w = ((raw_w * scale).round().max(1.0)) as u32;
    let src_h = ((raw_h * scale).round().max(1.0)) as u32;
    let (dst_w, dst_h) = fit_dimensions(src_w, src_h, cx)?;

    let stride = (dst_w as usize)
        .checked_mul(4)
        .ok_or_else(|| "Stride overflow".to_string())?;
    let buf_len = stride
        .checked_mul(dst_h as usize)
        .ok_or_else(|| "Buffer length overflow".to_string())?;
    let mut buf = vec![0u8; buf_len];

    let bmp = unsafe {
        (api.bitmap_create_ex)(
            dst_w as c_int,
            dst_h as c_int,
            FPDF_BITMAP_BGRA,
            buf.as_mut_ptr().cast(),
            stride as c_int,
        )
    };
    if bmp.is_null() {
        return Err("FPDFBitmap_CreateEx failed".to_string());
    }
    let bmp_guard = ScopedBitmap { api, bmp };

    unsafe {
        (api.bitmap_fill_rect)(bmp_guard.bmp, 0, 0, dst_w as c_int, dst_h as c_int, 0xFFFFFFFF);
        (api.render_page_bitmap)(
            bmp_guard.bmp,
            page_guard.page,
            0,
            0,
            dst_w as c_int,
            dst_h as c_int,
            0,
            FPDF_ANNOT | FPDF_PRINTING,
        );
    }

    drop(bmp_guard);
    drop(page_guard);
    drop(doc_guard);

    ThumbBitmap::from_bgra_premul(dst_w, dst_h, buf, false)
}
