use std::io::Read;
use std::sync::{Arc, OnceLock};

use flate2::read::GzDecoder;
use resvg::{tiny_skia, usvg};
use windows::Win32::System::Com::IStream;

use crate::bitmap::{ThumbBitmap, clamp_cx, fit_dimensions};
use crate::stream::{SMALL_MAX_STREAM_BYTES, StreamReader};

const MAX_SVGZ_UNCOMPRESSED_BYTES: usize = 32 * 1024 * 1024;

static FONT_DB: OnceLock<Arc<usvg::fontdb::Database>> = OnceLock::new();

fn shared_fontdb() -> Arc<usvg::fontdb::Database> {
    FONT_DB
        .get_or_init(|| {
            let mut db = usvg::fontdb::Database::new();
            db.load_system_fonts();
            Arc::new(db)
        })
        .clone()
}

pub fn decode_stream(stream: &IStream, cx: u32) -> Result<ThumbBitmap, String> {
    let reader = StreamReader::open(stream)?;
    let bytes = reader.read_all_capped(SMALL_MAX_STREAM_BYTES)?;
    decode_bytes(&bytes, cx)
}

pub fn decode_bytes(raw: &[u8], cx: u32) -> Result<ThumbBitmap, String> {
    let xml_owned: Vec<u8>;
    let svg_data: &[u8] = if raw.starts_with(&[0x1F, 0x8B]) {
        let mut decoder = GzDecoder::new(raw).take((MAX_SVGZ_UNCOMPRESSED_BYTES + 1) as u64);
        let mut out = Vec::new();
        decoder
            .read_to_end(&mut out)
            .map_err(|e| format!("Failed to decompress SVGZ: {e}"))?;
        if out.len() > MAX_SVGZ_UNCOMPRESSED_BYTES {
            return Err("SVGZ decompressed output exceeds 32 MiB limit".to_string());
        }
        xml_owned = out;
        &xml_owned
    } else {
        raw
    };

    let mut opts = usvg::Options {
        fontdb: shared_fontdb(),
        ..Default::default()
    };
    // Security: block all external file/network/UNC href resolution inside thumbnails.
    opts.image_href_resolver.resolve_string = Box::new(|_, _| None);

    let tree =
        usvg::Tree::from_data(svg_data, &opts).map_err(|e| format!("SVG parse failed: {e}"))?;
    let size = tree.size();
    let src_w = size.width().max(1.0);
    let src_h = size.height().max(1.0);

    let target_cx = clamp_cx(cx);
    // Fit vector SVG directly to the requested thumbnail size `target_cx` while keeping aspect ratio.
    let scale = (target_cx as f32) / src_w.max(src_h);
    let dst_w = ((src_w * scale).round().max(1.0) as u32).min(target_cx);
    let dst_h = ((src_h * scale).round().max(1.0) as u32).min(target_cx);
    let (dst_w, dst_h) = fit_dimensions(dst_w, dst_h, target_cx)?;

    let mut pixmap = tiny_skia::Pixmap::new(dst_w, dst_h)
        .ok_or_else(|| "Failed to allocate tiny-skia Pixmap".to_string())?;
    let sx = (dst_w as f32) / src_w;
    let sy = (dst_h as f32) / src_h;
    resvg::render(&tree, tiny_skia::Transform::from_scale(sx, sy), &mut pixmap.as_mut());

    // tiny_skia outputs premultiplied RGBA8: pass as premul to avoid double premultiplication.
    ThumbBitmap::from_rgba_premul(dst_w, dst_h, pixmap.take(), true)
}
