use windows::Win32::System::Com::IStream;

use craft_cdr::{Limits, PreviewFormat};

use crate::bitmap::ThumbBitmap;
use crate::stream::{DEFAULT_MAX_STREAM_BYTES, StreamReader};

pub fn decode_stream(stream: &IStream) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let bytes = reader.read_all_capped(DEFAULT_MAX_STREAM_BYTES)?;
    decode_bytes(&bytes)
}

pub fn decode_bytes(bytes: &[u8]) -> Result<ThumbBitmap, String> {
    let mut limits = Limits::default();
    limits.input = DEFAULT_MAX_STREAM_BYTES;
    limits.inflated = DEFAULT_MAX_STREAM_BYTES;
    limits.member = 32 * 1024 * 1024;

    let preview = craft_cdr::preview(bytes, limits)
        .map_err(|e| format!("CorelDRAW preview parse failed: {e}"))?
        .ok_or_else(|| "No embedded preview found in CorelDRAW file".to_string())?;

    let fmt = match preview.format {
        PreviewFormat::Png => image::ImageFormat::Png,
        PreviewFormat::Bmp => image::ImageFormat::Bmp,
    };

    let img = image::load_from_memory_with_format(&preview.bytes, fmt)
        .map_err(|e| format!("Failed decoding CDR preview image: {e}"))?;
    ThumbBitmap::from_dynamic_image(img)
}
