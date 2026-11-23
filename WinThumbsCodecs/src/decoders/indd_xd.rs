use std::io::{Cursor, Read};

use base64::Engine;
use windows::Win32::System::Com::IStream;
use zip::ZipArchive;

use crate::bitmap::ThumbBitmap;
use crate::stream::{SMALL_MAX_STREAM_BYTES, StreamReader, find_subslice};

const MAX_INDD_SCAN_BYTES: u64 = 64 * 1024 * 1024;
const MAX_XMP_IMAGE_B64_BYTES: usize = 8 * 1024 * 1024;
const MAX_ZIP_ENTRIES: usize = 256;
const MAX_ZIP_ENTRY_BYTES: u64 = 16 * 1024 * 1024;

pub fn decode_indd_or_idml_stream(stream: &IStream) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let prefix = reader.read_prefix(4)?;

    if prefix.starts_with(b"PK\x03\x04") {
        let bytes = reader.read_all_capped(crate::stream::DEFAULT_MAX_STREAM_BYTES)?;
        return decode_idml_bytes(&bytes);
    }

    for (start_tag, end_tag) in [
        (&b"<xmpGImg:image>"[..], &b"</xmpGImg:image>"[..]),
        (&b"<xapGImg:image>"[..], &b"</xapGImg:image>"[..]),
    ] {
        if let Some(payload) = reader.scan_for_bounded_tag(
            MAX_INDD_SCAN_BYTES,
            start_tag,
            end_tag,
            MAX_XMP_IMAGE_B64_BYTES,
        )? {
            return decode_xmp_base64_jpeg(&payload);
        }
    }

    Err("No embedded XMP thumbnail found in INDD file".to_string())
}

pub fn decode_indd_bytes(bytes: &[u8]) -> Result<ThumbBitmap, String> {
    if bytes.starts_with(b"PK\x03\x04") {
        return decode_idml_bytes(bytes);
    }

    extract_xmp_jpeg_from_slice(bytes)
        .ok_or_else(|| "No embedded XMP thumbnail found in INDD bytes".to_string())
}

pub fn decode_idml_bytes(bytes: &[u8]) -> Result<ThumbBitmap, String> {
    let mut archive =
        ZipArchive::new(Cursor::new(bytes)).map_err(|e| format!("Invalid IDML archive: {e}"))?;
    let count = archive.len().min(MAX_ZIP_ENTRIES);

    // Check direct preview entries if present first.
    for candidate in ["META-INF/thumbnail.png", "thumbnail.png", "Preview.jpg", "preview.jpg"] {
        if let Ok(entry) = archive.by_name(candidate) {
            let raw = read_zip_entry_capped(entry)?;
            if let Ok(img) = image::load_from_memory(&raw) {
                return ThumbBitmap::from_dynamic_image(img);
            }
        }
    }

    for idx in 0..count {
        let entry = archive
            .by_index(idx)
            .map_err(|e| format!("Failed reading IDML entry: {e}"))?;
        let name = entry.name().to_ascii_lowercase();
        if entry.is_dir() || (!name.ends_with(".xml") && !name.ends_with(".xmp")) {
            continue;
        }
        if entry.size() > MAX_ZIP_ENTRY_BYTES {
            continue;
        }
        let Ok(content) = read_zip_entry_capped(entry) else {
            continue;
        };
        if let Some(bmp) = extract_xmp_jpeg_from_slice(&content) {
            return Ok(bmp);
        }
    }

    Err("No embedded preview found in IDML archive".to_string())
}

pub fn decode_xd_stream(stream: &IStream) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let bytes = reader.read_all_capped(SMALL_MAX_STREAM_BYTES)?;
    decode_xd_bytes(&bytes)
}

pub fn decode_xd_bytes(bytes: &[u8]) -> Result<ThumbBitmap, String> {
    let mut archive =
        ZipArchive::new(Cursor::new(bytes)).map_err(|e| format!("Invalid XD archive: {e}"))?;

    for name in ["thumbnail.png", "preview.png"] {
        if let Ok(entry) = archive.by_name(name) {
            let png_bytes = read_zip_entry_capped(entry)?;
            let img = image::load_from_memory_with_format(&png_bytes, image::ImageFormat::Png)
                .map_err(|e| format!("Invalid XD thumbnail PNG: {e}"))?;
            return ThumbBitmap::from_dynamic_image(img);
        }
    }

    Err("No thumbnail.png or preview.png found in XD document".to_string())
}

fn read_zip_entry_capped<R: Read>(entry: zip::read::ZipFile<'_, R>) -> Result<Vec<u8>, String> {
    if entry.size() > MAX_ZIP_ENTRY_BYTES {
        return Err("ZIP entry exceeds maximum allowed size".to_string());
    }
    let mut buf = Vec::with_capacity((entry.size() as usize).min(1024 * 1024));
    entry
        .take(MAX_ZIP_ENTRY_BYTES + 1)
        .read_to_end(&mut buf)
        .map_err(|e| format!("Failed reading ZIP entry: {e}"))?;
    if buf.len() as u64 > MAX_ZIP_ENTRY_BYTES {
        return Err("Decompressed ZIP entry exceeded safety cap".to_string());
    }
    Ok(buf)
}

fn extract_xmp_jpeg_from_slice(bytes: &[u8]) -> Option<ThumbBitmap> {
    for (start_tag, end_tag) in [
        (&b"<xmpGImg:image>"[..], &b"</xmpGImg:image>"[..]),
        (&b"<xapGImg:image>"[..], &b"</xapGImg:image>"[..]),
    ] {
        let Some(start) = find_subslice(bytes, start_tag) else {
            continue;
        };
        let after = &bytes[start + start_tag.len()..];
        let search_slice = &after[..after.len().min(MAX_XMP_IMAGE_B64_BYTES)];
        let Some(end) = find_subslice(search_slice, end_tag) else {
            continue;
        };
        if let Ok(bmp) = decode_xmp_base64_jpeg(&search_slice[..end]) {
            return Some(bmp);
        }
    }
    None
}

fn decode_xmp_base64_jpeg(raw_b64: &[u8]) -> Result<ThumbBitmap, String> {
    // Strip XML-escaped line breaks (`&#xA;` / `&#xD;`) that Adobe InDesign injects into base64.
    let mut cleaned = Vec::with_capacity(raw_b64.len());
    let mut i = 0usize;
    while i < raw_b64.len() {
        let rem = &raw_b64[i..];
        if rem.len() >= 5
            && (rem[..5].eq_ignore_ascii_case(b"&#xA;") || rem[..5].eq_ignore_ascii_case(b"&#xD;"))
        {
            i += 5;
            continue;
        }
        let b = raw_b64[i];
        if !matches!(b, b' ' | b'\r' | b'\n' | b'\t') {
            cleaned.push(b);
        }
        i += 1;
    }

    let jpeg_bytes = base64::engine::general_purpose::STANDARD
        .decode(&cleaned)
        .map_err(|e| format!("Invalid base64 XMP thumbnail: {e}"))?;
    let img = image::load_from_memory_with_format(&jpeg_bytes, image::ImageFormat::Jpeg)
        .map_err(|e| format!("Invalid JPEG XMP thumbnail: {e}"))?;
    ThumbBitmap::from_dynamic_image(img)
}
