use windows::Win32::System::Com::IStream;

use crate::bitmap::ThumbBitmap;
use crate::stream::{DEFAULT_MAX_STREAM_BYTES, StreamReader};

pub fn decode_stream(stream: &IStream) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let bytes = reader.read_all_capped(DEFAULT_MAX_STREAM_BYTES)?;
    decode_bytes(&bytes)
}

pub fn decode_bytes(bytes: &[u8]) -> Result<ThumbBitmap, String> {
    let preview = craft_affinity::preview(bytes)
        .map_err(|e| format!("Affinity preview parse failed: {e}"))?;
    let img = image::load_from_memory_with_format(preview.png, image::ImageFormat::Png)
        .map_err(|e| format!("Failed decoding Affinity PNG preview: {e}"))?;
    ThumbBitmap::from_dynamic_image(img)
}
