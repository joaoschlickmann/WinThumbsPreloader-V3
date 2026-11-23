use windows::Win32::System::Com::IStream;

use crate::CodecKind;
use crate::bitmap::ThumbBitmap;

pub mod affinity;
pub mod cdr;
pub mod eps;
pub mod indd_xd;
pub mod pdf_ai;
pub mod psd_psb;
pub mod raw;
pub mod svg;

pub fn decode_from_stream(
    kind: CodecKind,
    stream: &IStream,
    cx: u32,
) -> Result<ThumbBitmap, String> {
    match kind {
        CodecKind::PsdPsb => psd_psb::decode_stream(stream, cx),
        CodecKind::Pdf => pdf_ai::decode_pdf_stream(stream, cx),
        CodecKind::Illustrator => pdf_ai::decode_ai_stream(stream, cx),
        CodecKind::Eps => eps::decode_stream(stream, cx),
        CodecKind::InDesign => indd_xd::decode_indd_or_idml_stream(stream),
        CodecKind::AdobeXd => indd_xd::decode_xd_stream(stream),
        CodecKind::Svg => svg::decode_stream(stream, cx),
        CodecKind::CorelDraw => cdr::decode_stream(stream),
        CodecKind::Affinity => affinity::decode_stream(stream),
        CodecKind::CameraRaw => raw::decode_stream(stream, cx),
    }
}
