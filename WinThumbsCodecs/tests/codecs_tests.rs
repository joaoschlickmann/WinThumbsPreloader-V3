use std::io::{Cursor, Write};
use std::path::PathBuf;

use WinThumbsCodecs::{
    CodecKind, DllCanUnloadNow, DllGetClassObject, bitmap,
    decoders::{affinity, cdr, eps, indd_xd, pdf_ai, psd_psb, raw, svg},
    set_test_codecs_dir,
};
use craft_psd::{ColorMode, ImageResource, LayerSpec, PixelData, PsdBuilder, Version, resources};
use flate2::{Compression as GzCompression, write::GzEncoder};
use windows::Win32::Foundation::{CLASS_E_CLASSNOTAVAILABLE, HGLOBAL, S_OK};
use windows::Win32::Graphics::Gdi::{BITMAP, DeleteObject, GetObjectW, HBITMAP, HGDIOBJ};
use windows::Win32::System::Com::StructuredStorage::CreateStreamOnHGlobal;
use windows::Win32::System::Com::{IClassFactory, IStream};
use windows::Win32::UI::Shell::PropertiesSystem::IInitializeWithStream;
use windows::Win32::UI::Shell::{IThumbnailProvider, WTS_ALPHATYPE};
use windows::core::Interface;
use zip::CompressionMethod;
use zip::write::SimpleFileOptions;

fn ensure_test_codecs_dir() {
    let dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("prebuilt")
        .join("win-x64");
    set_test_codecs_dir(dir);
}

fn encode_png_rgba(w: u32, h: u32, rgba: &[u8]) -> Vec<u8> {
    let img = image::RgbaImage::from_raw(w, h, rgba.to_vec()).expect("valid rgba");
    let mut out = Cursor::new(Vec::new());
    image::DynamicImage::ImageRgba8(img)
        .write_to(&mut out, image::ImageFormat::Png)
        .expect("encode png");
    out.into_inner()
}

fn encode_jpeg_rgb(w: u32, h: u32, rgb: &[u8]) -> Vec<u8> {
    let img = image::RgbImage::from_raw(w, h, rgb.to_vec()).expect("valid rgb");
    let mut out = Cursor::new(Vec::new());
    image::DynamicImage::ImageRgb8(img)
        .write_to(&mut out, image::ImageFormat::Jpeg)
        .expect("encode jpeg");
    out.into_inner()
}

fn encode_tiff_rgb(w: u32, h: u32, rgb: &[u8]) -> Vec<u8> {
    let img = image::RgbImage::from_raw(w, h, rgb.to_vec()).expect("valid rgb");
    let mut out = Cursor::new(Vec::new());
    image::DynamicImage::ImageRgb8(img)
        .write_to(&mut out, image::ImageFormat::Tiff)
        .expect("encode tiff");
    out.into_inner()
}

fn make_minimal_pdf() -> Vec<u8> {
    // Minimal 1-page PDF 1.4 drawing a solid red square across the 100x100 page.
    let stream_body = b"1 0 0 rg 0 0 100 100 re f\n";
    let mut out = Vec::new();
    out.extend_from_slice(b"%PDF-1.4\n");

    let mut offsets = Vec::new();
    let push_obj = |buf: &mut Vec<u8>, offsets: &mut Vec<usize>, id: usize, body: &[u8]| {
        offsets.push(buf.len());
        buf.extend_from_slice(format!("{id} 0 obj\n").as_bytes());
        buf.extend_from_slice(body);
        buf.extend_from_slice(b"\nendobj\n");
    };

    push_obj(&mut out, &mut offsets, 1, b"<< /Type /Catalog /Pages 2 0 R >>");
    push_obj(&mut out, &mut offsets, 2, b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
    push_obj(
        &mut out,
        &mut offsets,
        3,
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
    );

    let mut content_obj = format!("<< /Length {} >>\nstream\n", stream_body.len()).into_bytes();
    content_obj.extend_from_slice(stream_body);
    content_obj.extend_from_slice(b"endstream");
    push_obj(&mut out, &mut offsets, 4, &content_obj);

    let xref_pos = out.len();
    out.extend_from_slice(format!("xref\n0 {}\n0000000000 65535 f \n", offsets.len() + 1).as_bytes());
    for off in &offsets {
        out.extend_from_slice(format!("{off:010} 00000 n \n").as_bytes());
    }
    out.extend_from_slice(
        format!(
            "trailer\n<< /Size {} /Root 1 0 R >>\nstartxref\n{xref_pos}\n%%EOF\n",
            offsets.len() + 1
        )
        .as_bytes(),
    );
    out
}

fn make_pure_vector_eps() -> Vec<u8> {
    b"%!PS-Adobe-3.0 EPSF-3.0\n\
%%BoundingBox: 0 0 100 100\n\
0 1 0 setrgbcolor\n\
0 0 100 100 rectfill\n\
showpage\n\
%%EOF\n"
        .to_vec()
}

fn make_dos_eps_with_tiff(tiff: &[u8]) -> Vec<u8> {
    let ps = b"%!PS-Adobe-3.0 EPSF-3.0\n%%BoundingBox: 0 0 16 16\n%%EOF\n";
    let ps_off = 30u32;
    let ps_len = ps.len() as u32;
    let tiff_off = ps_off + ps_len;
    let tiff_len = tiff.len() as u32;

    let mut out = Vec::with_capacity(tiff_off as usize + tiff.len());
    out.extend_from_slice(&[0xC5, 0xD0, 0xD3, 0xC6]);
    out.extend_from_slice(&ps_off.to_le_bytes());
    out.extend_from_slice(&ps_len.to_le_bytes());
    out.extend_from_slice(&0u32.to_le_bytes()); // WMF offset
    out.extend_from_slice(&0u32.to_le_bytes()); // WMF len
    out.extend_from_slice(&tiff_off.to_le_bytes());
    out.extend_from_slice(&tiff_len.to_le_bytes());
    out.extend_from_slice(&0xFFFFu16.to_le_bytes());
    out.extend_from_slice(ps);
    out.extend_from_slice(tiff);
    out
}

fn make_epsi() -> Vec<u8> {
    b"%!PS-Adobe-3.0 EPSF-3.0\n\
%%BoundingBox: 0 0 8 2\n\
%%BeginPreview: 8 2 1 2\n\
% FF\n\
% 00\n\
%%EndPreview\n\
%%EOF\n"
        .to_vec()
}

fn make_indd_with_xmp_jpeg(jpeg: &[u8]) -> Vec<u8> {
    use base64::Engine;
    let b64 = base64::engine::general_purpose::STANDARD.encode(jpeg);
    let mid = b64.len() / 2;
    let with_xml_newlines = format!("{}&#xA;{}", &b64[..mid], &b64[mid..]);
    let mut out = vec![0u8; 64]; // binary INDD lead bytes
    out.extend_from_slice(b"<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
    out.extend_from_slice(b"<xmpGImg:image>");
    out.extend_from_slice(with_xml_newlines.as_bytes());
    out.extend_from_slice(b"</xmpGImg:image><?xpacket end=\"w\"?>");
    out
}

fn make_zip_with_entries(entries: &[(&str, &[u8], CompressionMethod)]) -> Vec<u8> {
    let mut buf = Cursor::new(Vec::new());
    {
        let mut writer = zip::ZipWriter::new(&mut buf);
        for &(name, data, method) in entries {
            let opts = SimpleFileOptions::default().compression_method(method);
            writer.start_file(name, opts).expect("start zip file");
            writer.write_all(data).expect("write zip entry");
        }
        writer.finish().expect("finish zip");
    }
    buf.into_inner()
}

fn make_affinity_with_png(png: &[u8]) -> Vec<u8> {
    let mut out = Vec::new();
    out.extend_from_slice(b"\x00\xffKA");
    out.extend_from_slice(&10u16.to_le_bytes()); // container version 10
    out.extend_from_slice(&0u16.to_le_bytes()); // flags
    out.extend_from_slice(b"nsrP");
    out.extend_from_slice(b"#Inf");
    out.extend_from_slice(&0u64.to_le_bytes()); // fat_offset
    out.extend_from_slice(&72u64.to_le_bytes()); // thumbnail_offset
    out.extend_from_slice(&[0u8; 16]);
    out.extend_from_slice(&0u64.to_le_bytes()); // saved
    out.extend_from_slice(&[0u8; 8]);
    out.extend_from_slice(b"Prot");
    out.resize(72, 0);

    out.extend_from_slice(b"\xff\xff\xff\xffThmb");
    out.extend_from_slice(&1u32.to_le_bytes());
    out.extend_from_slice(&(png.len() as u32 + 13).to_le_bytes());
    out.extend_from_slice(&29u32.to_le_bytes());
    out.extend_from_slice(&0u32.to_le_bytes());
    out.extend_from_slice(&(png.len() as u32).to_le_bytes());
    out.push(1u8);
    out.extend_from_slice(png);
    out
}

fn make_cdr_riff_disp(w: u32, h: u32) -> Vec<u8> {
    let stride = ((w * 3) + 3) & !3;
    let pixel_bytes = (stride * h) as usize;
    let mut disp_body = Vec::with_capacity(4 + 40 + pixel_bytes);
    disp_body.extend_from_slice(&[8, 0, 0, 0]);
    disp_body.extend_from_slice(&40u32.to_le_bytes());
    disp_body.extend_from_slice(&(w as i32).to_le_bytes());
    disp_body.extend_from_slice(&(h as i32).to_le_bytes());
    disp_body.extend_from_slice(&1u16.to_le_bytes()); // planes
    disp_body.extend_from_slice(&24u16.to_le_bytes()); // bpp
    disp_body.extend_from_slice(&0u32.to_le_bytes()); // BI_RGB
    disp_body.extend_from_slice(&(pixel_bytes as u32).to_le_bytes());
    disp_body.extend_from_slice(&[0u8; 16]);
    for _y in 0..h {
        for _x in 0..w {
            disp_body.extend_from_slice(&[200, 100, 40]); // BGR
        }
        disp_body.resize(disp_body.len() + (stride - w * 3) as usize, 0);
    }

    let chunk_pad = disp_body.len() & 1;
    let form_len = 4 + 8 + disp_body.len() + chunk_pad;
    let mut out = Vec::with_capacity(8 + form_len);
    out.extend_from_slice(b"RIFF");
    out.extend_from_slice(&(form_len as u32).to_le_bytes());
    out.extend_from_slice(b"CDR8");
    out.extend_from_slice(b"DISP");
    out.extend_from_slice(&(disp_body.len() as u32).to_le_bytes());
    out.extend_from_slice(&disp_body);
    if chunk_pad == 1 {
        out.push(0);
    }
    out
}

fn make_synthetic_dng_with_jpeg(w: u32, h: u32, orientation: u16, jpeg: &[u8]) -> Vec<u8> {
    // Little-endian TIFF with 6 IFD0 tags in ascending tag order.
    let ifd_offset = 8u32;
    let count = 6u16;
    let jpeg_offset = ifd_offset + 2 + (count as u32 * 12) + 4;

    let mut out = Vec::with_capacity(jpeg_offset as usize + jpeg.len());
    out.extend_from_slice(b"II*\0");
    out.extend_from_slice(&ifd_offset.to_le_bytes());
    out.extend_from_slice(&count.to_le_bytes());

    let push_tag = |buf: &mut Vec<u8>, tag: u16, ty: u16, cnt: u32, val: u32| {
        buf.extend_from_slice(&tag.to_le_bytes());
        buf.extend_from_slice(&ty.to_le_bytes());
        buf.extend_from_slice(&cnt.to_le_bytes());
        buf.extend_from_slice(&val.to_le_bytes());
    };

    push_tag(&mut out, 0x0100, 4, 1, w); // ImageWidth
    push_tag(&mut out, 0x0101, 4, 1, h); // ImageLength
    push_tag(&mut out, 0x0112, 3, 1, u32::from(orientation)); // Orientation
    push_tag(&mut out, 0x0201, 4, 1, jpeg_offset); // JPEGInterchangeFormat
    push_tag(&mut out, 0x0202, 4, 1, jpeg.len() as u32); // JPEGInterchangeFormatLength
    // DNGVersion (BYTE[4] inline in value field: 1, 4, 0, 0)
    push_tag(&mut out, 0xC612, 1, 4, u32::from_le_bytes([1, 4, 0, 0]));
    out.extend_from_slice(&0u32.to_le_bytes()); // next IFD = 0
    out.extend_from_slice(jpeg);
    out
}

fn make_stream_from_bytes(data: &[u8]) -> IStream {
    let stream = unsafe { CreateStreamOnHGlobal(HGLOBAL::default(), true) }.expect("CreateStreamOnHGlobal");
    let mut written = 0u32;
    unsafe {
        stream
            .Write(data.as_ptr().cast(), data.len() as u32, Some(&mut written))
            .ok()
            .expect("IStream::Write");
        assert_eq!(written as usize, data.len());
    }
    stream
}

fn make_cmyk_psd(width: u32, height: u32, c_ink: u8, m_ink: u8, y_ink: u8, k_ink: u8) -> Vec<u8> {
    // PixelData::Cmyka8 expects interleaved ink amounts (0 = no ink, 255 = full ink); PsdBuilder inverts them.
    let mut b = PsdBuilder::new(width, height).color_mode(ColorMode::Cmyk);
    let n = (width * height) as usize;
    let mut merged = Vec::with_capacity(n * 5);
    for _ in 0..n {
        merged.extend_from_slice(&[c_ink, m_ink, y_ink, k_ink, 255]);
    }
    b.composite(PixelData::Cmyka8(merged));
    b.to_bytes().expect("build cmyk psd")
}

#[test]
fn psd_psb_decodes_rgb_cmyk_1036_1033_psb_and_rejects_oversized_header() {
    // 1. RGB PSD v1
    let mut b = PsdBuilder::new(8, 8);
    let mut rgba = Vec::with_capacity(8 * 8 * 4);
    for _ in 0..(8 * 8) {
        rgba.extend_from_slice(&[240, 20, 30, 255]);
    }
    b.push_layer(LayerSpec::new("Layer", 0, 0, 8, 8, PixelData::Rgba8(rgba.clone())));
    b.composite(PixelData::Rgba8(rgba));
    let rgb_psd = b.to_bytes().expect("rgb psd");
    let decoded = psd_psb::decode_bytes(&rgb_psd, 256).expect("decode rgb psd");
    assert_eq!((decoded.width, decoded.height), (8, 8));
    assert!(decoded.pixels[0] > 200 && decoded.pixels[1] < 60);

    // 2. CMYK PSD (verify 255-v inversion + ICC produces white paper, NOT black)
    let white_cmyk_psd = make_cmyk_psd(8, 8, 0, 0, 0, 0);
    let white_bmp = psd_psb::decode_bytes(&white_cmyk_psd, 256).expect("decode cmyk white");
    assert!(
        white_bmp.pixels[0] > 235 && white_bmp.pixels[1] > 235 && white_bmp.pixels[2] > 235,
        "Expected white CMYK paper, got RGB=({},{},{})",
        white_bmp.pixels[0],
        white_bmp.pixels[1],
        white_bmp.pixels[2]
    );

    // 3. Embedded 1036 JFIF & 1033 BGR-swapped thumbnail without merged composite
    let jpeg_rgb = encode_jpeg_rgb(16, 16, &vec![220, 20, 20].repeat(16 * 16));
    let thumb_payload = resources::Thumbnail::jfif_bytes(16, 16, &jpeg_rgb).expect("thumb payload");
    let b1033 = PsdBuilder::new(32, 32)
        .resource(ImageResource::new(resources::ids::THUMBNAIL_PS4, thumb_payload))
        .resource(resources::version_info_resource(false)); // mark no real merged data
    let psd_1033 = b1033.to_bytes().expect("psd 1033");
    let bmp_1033 = psd_psb::decode_bytes(&psd_1033, 16).expect("decode 1033");
    // Input JPEG was red (220,20,20); treated as BGR in 1033 -> swapped to blue-dominant!
    assert!(bmp_1033.pixels[2] > 180 && bmp_1033.pixels[0] < 60);

    // 4. PSB v2 (64-bit)
    let mut psb_b = PsdBuilder::new(12, 10).version(Version::Psb);
    psb_b.composite(PixelData::Rgba8(vec![30, 210, 60, 255].repeat(12 * 10)));
    let psb_bytes = psb_b.to_bytes().expect("psb bytes");
    let psb_bmp = psd_psb::decode_bytes(&psb_bytes, 256).expect("decode psb");
    assert_eq!((psb_bmp.width, psb_bmp.height), (12, 10));

    // 5. Adversarial bogus 300,000 x 300,000 header without merged data or thumb -> clean Err, no OOM
    let mut hostile = vec![0u8; 64];
    hostile[..4].copy_from_slice(b"8BPS");
    hostile[4..6].copy_from_slice(&1u16.to_be_bytes());
    hostile[12..14].copy_from_slice(&3u16.to_be_bytes());
    hostile[14..18].copy_from_slice(&300_000u32.to_be_bytes());
    hostile[18..22].copy_from_slice(&300_000u32.to_be_bytes());
    hostile[22..24].copy_from_slice(&8u16.to_be_bytes());
    hostile[24..26].copy_from_slice(&3u16.to_be_bytes());
    assert!(psd_psb::decode_bytes(&hostile, 256).is_err());
}

#[test]
fn pdf_and_ai_render_cleanly() {
    ensure_test_codecs_dir();
    let pdf = make_minimal_pdf();
    let bmp = pdf_ai::render_pdf_bytes(&pdf, 128).expect("render minimal pdf");
    assert_eq!((bmp.width, bmp.height), (128, 128));
    let mid = ((64 * 128 + 64) * 4) as usize;
    // PDFium BGRA output: red square => B < 20, G < 20, R > 230
    assert!(bmp.pixels[mid] < 20 && bmp.pixels[mid + 2] > 230);

    // PDF-based .ai
    let mut ai_pdf = pdf.clone();
    ai_pdf.extend_from_slice(b"% /AIPrivateData\n");
    let ai_bmp = pdf_ai::decode_ai_bytes(&ai_pdf, 96).expect("render pdf-compatible ai");
    assert_eq!((ai_bmp.width, ai_bmp.height), (96, 96));

    // Legacy PostScript .ai -> routed through craft-ps + PDFium
    let legacy_ai = b"%!PS-Adobe-3.0\n%AI5_FileFormat 2.0\n%%BoundingBox: 0 0 100 100\n0 0 1 setrgbcolor\n0 0 100 100 rectfill\nshowpage\n%%EOF\n";
    let legacy_bmp = pdf_ai::decode_ai_bytes(legacy_ai, 96).expect("render legacy ai");
    assert_eq!((legacy_bmp.width, legacy_bmp.height), (96, 96));
    let mid96 = ((48 * 96 + 48) * 4) as usize;
    assert!(legacy_bmp.pixels[mid96] > 220 && legacy_bmp.pixels[mid96 + 2] < 30);
}

#[test]
fn eps_decodes_dos_tiff_epsi_pure_vector_and_aborts_infinite_loop() {
    ensure_test_codecs_dir();

    // 1. DOS EPS with TIFF preview
    let tiff = encode_tiff_rgb(16, 12, &vec![40, 120, 220].repeat(16 * 12));
    let dos_eps = make_dos_eps_with_tiff(&tiff);
    let dos_bmp = eps::decode_bytes(&dos_eps, 128).expect("decode dos eps tiff");
    assert_eq!((dos_bmp.width, dos_bmp.height), (16, 12));

    // 2. ASCII EPSI preview
    let epsi_bmp = eps::decode_bytes(&make_epsi(), 128).expect("decode epsi");
    assert_eq!((epsi_bmp.width, epsi_bmp.height), (8, 2));

    // 3. Pure vector PostScript EPS without embedded preview (green square)
    let ps_bmp = eps::decode_bytes(&make_pure_vector_eps(), 96).expect("decode pure vector eps");
    assert_eq!((ps_bmp.width, ps_bmp.height), (96, 96));
    let mid = ((48 * 96 + 48) * 4) as usize;
    assert!(ps_bmp.pixels[mid + 1] > 220);

    // 4. Adversarial infinite-loop EPS must terminate cleanly with Err
    let loop_eps = b"%!PS-Adobe-3.0 EPSF-3.0\n%%BoundingBox: 0 0 64 64\n{ } loop\n%%EOF\n";
    assert!(eps::decode_bytes(loop_eps, 96).is_err());
}

#[test]
fn indd_idml_xd_and_zip_bomb_protection() {
    let jpeg = encode_jpeg_rgb(16, 12, &vec![10, 180, 220].repeat(16 * 12));
    let indd = make_indd_with_xmp_jpeg(&jpeg);
    let indd_bmp = indd_xd::decode_indd_bytes(&indd).expect("decode indd");
    assert_eq!((indd_bmp.width, indd_bmp.height), (16, 12));

    let xmp_xml = format!(
        "<?xpacket?><x:xmpmeta><xmpGImg:image>{}</xmpGImg:image></x:xmpmeta>",
        base64::Engine::encode(&base64::engine::general_purpose::STANDARD, &jpeg)
    );
    let idml = make_zip_with_entries(&[
        ("META-INF/metadata.xml", xmp_xml.as_bytes(), CompressionMethod::Deflated),
    ]);
    let idml_bmp = indd_xd::decode_idml_bytes(&idml).expect("decode idml");
    assert_eq!((idml_bmp.width, idml_bmp.height), (16, 12));

    let png = encode_png_rgba(16, 16, &vec![20, 200, 90, 255].repeat(16 * 16));
    let xd = make_zip_with_entries(&[("thumbnail.png", &png, CompressionMethod::Stored)]);
    let xd_bmp = indd_xd::decode_xd_bytes(&xd).expect("decode xd");
    assert_eq!((xd_bmp.width, xd_bmp.height), (16, 16));

    // Zip bomb (> 16 MiB decompressed payload)
    let bomb_payload = vec![0u8; 17 * 1024 * 1024];
    let xd_bomb = make_zip_with_entries(&[
        ("thumbnail.png", &bomb_payload, CompressionMethod::Deflated),
    ]);
    assert!(indd_xd::decode_xd_bytes(&xd_bomb).is_err());
}

#[test]
fn svg_svgz_and_external_href_blocked() {
    let raw_svg = br##"<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><rect width="64" height="64" fill="#ff0000"/></svg>"##;
    let svg_bmp = svg::decode_bytes(raw_svg, 128).expect("decode svg");
    assert_eq!((svg_bmp.width, svg_bmp.height), (128, 128));
    assert_eq!(svg_bmp.pixels[0], 255);
    assert_eq!(svg_bmp.pixels[1], 0);

    let mut gz = GzEncoder::new(Vec::new(), GzCompression::default());
    gz.write_all(raw_svg).expect("gz write");
    let svgz = gz.finish().expect("gz finish");
    let svgz_bmp = svg::decode_bytes(&svgz, 64).expect("decode svgz");
    assert_eq!((svgz_bmp.width, svgz_bmp.height), (64, 64));

    // Malicious external file href must not fail or read external file
    let hostile_svg = br##"<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><image href="file:///C:/Windows/win.ini" width="32" height="32"/><rect width="32" height="32" fill="#00ff00"/></svg>"##;
    let safe_bmp = svg::decode_bytes(hostile_svg, 64).expect("safe svg with blocked external href");
    assert_eq!((safe_bmp.width, safe_bmp.height), (64, 64));
    assert_eq!(safe_bmp.pixels[1], 255);
}

#[test]
fn cdr_affinity_and_raw_decode_with_exif_orientation() {
    // 1. CDR ZIP & RIFF DISP
    let png = encode_png_rgba(16, 12, &vec![50, 90, 210, 255].repeat(16 * 12));
    let cdr_zip = make_zip_with_entries(&[
        ("content/riffData.cdr", b"RIFF\x04\x00\x00\x00CDRG", CompressionMethod::Stored),
        ("previews/thumbnail.png", &png, CompressionMethod::Deflated),
    ]);
    let cdr_zip_bmp = cdr::decode_bytes(&cdr_zip).expect("decode cdr zip");
    assert_eq!((cdr_zip_bmp.width, cdr_zip_bmp.height), (16, 12));

    let cdr_riff = make_cdr_riff_disp(16, 10);
    let cdr_riff_bmp = cdr::decode_bytes(&cdr_riff).expect("decode cdr riff disp");
    assert_eq!((cdr_riff_bmp.width, cdr_riff_bmp.height), (16, 10));

    // 2. Affinity container
    let aff = make_affinity_with_png(&png);
    let aff_bmp = affinity::decode_bytes(&aff).expect("decode affinity");
    assert_eq!((aff_bmp.width, aff_bmp.height), (16, 12));

    // 3. RAW (DNG container with 16x8 JPEG and Orientation=6 -> rotated to 8x16!)
    let raw_jpeg = encode_jpeg_rgb(16, 8, &vec![20, 140, 230].repeat(16 * 8));
    let dng = make_synthetic_dng_with_jpeg(16, 8, 6, &raw_jpeg);
    let raw_bmp = raw::decode_bytes(&dng, 128).expect("decode raw dng");
    assert_eq!((raw_bmp.width, raw_bmp.height), (8, 16));
}

#[test]
fn com_class_factory_and_thumbnail_provider_lifecycle_across_all_10_clsids() {
    ensure_test_codecs_dir();

    let png = encode_png_rgba(24, 16, &vec![10, 160, 220, 255].repeat(24 * 16));
    let jpeg = encode_jpeg_rgb(24, 16, &vec![200, 60, 30].repeat(24 * 16));
    let psd = {
        let mut b = PsdBuilder::new(16, 16);
        b.composite(PixelData::Rgba8(vec![100, 150, 200, 255].repeat(16 * 16)));
        b.to_bytes().expect("psd")
    };
    let pdf = make_minimal_pdf();
    let eps = make_pure_vector_eps();
    let indd = make_indd_with_xmp_jpeg(&jpeg);
    let xd = make_zip_with_entries(&[("thumbnail.png", &png, CompressionMethod::Stored)]);
    let svg_doc = br##"<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><circle cx="16" cy="16" r="14" fill="blue"/></svg>"##.to_vec();
    let cdr_doc = make_cdr_riff_disp(16, 16);
    let aff_doc = make_affinity_with_png(&png);
    let dng_doc = make_synthetic_dng_with_jpeg(24, 16, 1, &jpeg);

    let cases: &[(CodecKind, &[u8])] = &[
        (CodecKind::PsdPsb, &psd),
        (CodecKind::Pdf, &pdf),
        (CodecKind::Illustrator, &pdf),
        (CodecKind::Eps, &eps),
        (CodecKind::InDesign, &indd),
        (CodecKind::AdobeXd, &xd),
        (CodecKind::Svg, &svg_doc),
        (CodecKind::CorelDraw, &cdr_doc),
        (CodecKind::Affinity, &aff_doc),
        (CodecKind::CameraRaw, &dng_doc),
    ];

    assert_eq!(unsafe { DllCanUnloadNow() }, S_OK);

    // Unknown CLSID must fail cleanly.
    let unknown_clsid = windows::core::GUID::from_u128(0xdeadbeef_0000_0000_0000_000000000000);
    let mut null_ptr: *mut core::ffi::c_void = core::ptr::null_mut();
    assert_eq!(
        unsafe { DllGetClassObject(&unknown_clsid, &IClassFactory::IID, &mut null_ptr) },
        CLASS_E_CLASSNOTAVAILABLE
    );

    for &(kind, bytes) in cases {
        let clsid = kind.clsid();
        let mut factory_ptr: *mut core::ffi::c_void = core::ptr::null_mut();
        let hr = unsafe { DllGetClassObject(&clsid, &IClassFactory::IID, &mut factory_ptr) };
        assert_eq!(hr, S_OK, "DllGetClassObject failed for {kind:?}");
        let factory: IClassFactory = unsafe { IClassFactory::from_raw(factory_ptr) };

        let provider: IThumbnailProvider =
            unsafe { factory.CreateInstance(None) }.expect("CreateInstance IThumbnailProvider");
        let init: IInitializeWithStream = provider.cast().expect("QI IInitializeWithStream");

        let stream = make_stream_from_bytes(bytes);
        unsafe { init.Initialize(&stream, 0) }.expect("IInitializeWithStream::Initialize");

        let mut hbmp = HBITMAP::default();
        let mut alpha = WTS_ALPHATYPE::default();
        unsafe { provider.GetThumbnail(128, &mut hbmp, &mut alpha) }
            .unwrap_or_else(|e| panic!("GetThumbnail failed for {kind:?}: {e}"));
        assert!(!hbmp.is_invalid(), "HBITMAP invalid for {kind:?}");

        let mut bm = BITMAP::default();
        let obj_bytes = unsafe {
            GetObjectW(
                HGDIOBJ(hbmp.0),
                std::mem::size_of::<BITMAP>() as i32,
                Some((&mut bm as *mut BITMAP).cast()),
            )
        };
        assert!(obj_bytes > 0 && bm.bmWidth > 0 && bm.bmHeight > 0);
        assert!(bm.bmWidth <= 128 && bm.bmHeight <= 128);
        assert_eq!(bm.bmBitsPixel, 32);

        unsafe {
            let _ = DeleteObject(HGDIOBJ(hbmp.0));
        }

        // Verify bitmap::to_hbitmap consistency and clean COM teardown
        drop(init);
        drop(provider);
        drop(factory);
    }

    assert_eq!(unsafe { DllCanUnloadNow() }, S_OK);
    let _ = bitmap::clamp_cx(256);
}
