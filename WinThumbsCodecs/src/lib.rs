#![allow(non_snake_case)]

pub mod bitmap;
pub mod com_provider;
pub mod decoders;
pub mod stream;

use std::path::PathBuf;
use std::sync::atomic::{AtomicIsize, AtomicU32, Ordering};
use windows::Win32::Foundation::{
    CLASS_E_CLASSNOTAVAILABLE, E_INVALIDARG, E_POINTER, HMODULE, S_FALSE, S_OK, TRUE,
};
use windows::Win32::System::Com::IClassFactory;
use windows::Win32::System::LibraryLoader::{DisableThreadLibraryCalls, GetModuleFileNameW};
use windows::Win32::System::SystemServices::DLL_PROCESS_ATTACH;
use windows::core::{BOOL, GUID, HRESULT, Interface};

use com_provider::CodecClassFactory;

static DLL_MODULE: AtomicIsize = AtomicIsize::new(0);
static TEST_CODECS_DIR: std::sync::OnceLock<PathBuf> = std::sync::OnceLock::new();
pub static OBJECT_COUNT: AtomicU32 = AtomicU32::new(0);
pub static LOCK_COUNT: AtomicU32 = AtomicU32::new(0);

pub fn dec_saturating(counter: &AtomicU32) {
    let mut cur = counter.load(Ordering::Relaxed);
    while cur > 0 {
        match counter.compare_exchange_weak(cur, cur - 1, Ordering::Relaxed, Ordering::Relaxed) {
            Ok(_) => break,
            Err(next) => cur = next,
        }
    }
}

pub fn set_test_codecs_dir(dir: PathBuf) {
    if DLL_MODULE.load(Ordering::Relaxed) == 0 {
        let _ = TEST_CODECS_DIR.set(dir);
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CodecKind {
    PsdPsb,
    Pdf,
    Illustrator,
    Eps,
    InDesign,
    AdobeXd,
    Svg,
    CorelDraw,
    Affinity,
    CameraRaw,
}

impl CodecKind {
    pub const CLSID_PSD_PSB: GUID = GUID::from_u128(0xa7f3c101_6b4e_4d28_9e15_8c4a2f01b301);
    pub const CLSID_PDF: GUID = GUID::from_u128(0xa7f3c102_6b4e_4d28_9e15_8c4a2f01b302);
    pub const CLSID_ILLUSTRATOR: GUID = GUID::from_u128(0xa7f3c103_6b4e_4d28_9e15_8c4a2f01b303);
    pub const CLSID_EPS: GUID = GUID::from_u128(0xa7f3c104_6b4e_4d28_9e15_8c4a2f01b304);
    pub const CLSID_INDESIGN: GUID = GUID::from_u128(0xa7f3c105_6b4e_4d28_9e15_8c4a2f01b305);
    pub const CLSID_ADOBE_XD: GUID = GUID::from_u128(0xa7f3c106_6b4e_4d28_9e15_8c4a2f01b306);
    pub const CLSID_SVG: GUID = GUID::from_u128(0xa7f3c107_6b4e_4d28_9e15_8c4a2f01b307);
    pub const CLSID_COREL_DRAW: GUID = GUID::from_u128(0xa7f3c108_6b4e_4d28_9e15_8c4a2f01b308);
    pub const CLSID_AFFINITY: GUID = GUID::from_u128(0xa7f3c109_6b4e_4d28_9e15_8c4a2f01b309);
    pub const CLSID_CAMERA_RAW: GUID = GUID::from_u128(0xa7f3c10a_6b4e_4d28_9e15_8c4a2f01b30a);

    pub const ALL: [CodecKind; 10] = [
        CodecKind::PsdPsb,
        CodecKind::Pdf,
        CodecKind::Illustrator,
        CodecKind::Eps,
        CodecKind::InDesign,
        CodecKind::AdobeXd,
        CodecKind::Svg,
        CodecKind::CorelDraw,
        CodecKind::Affinity,
        CodecKind::CameraRaw,
    ];

    pub const fn clsid(self) -> GUID {
        match self {
            CodecKind::PsdPsb => Self::CLSID_PSD_PSB,
            CodecKind::Pdf => Self::CLSID_PDF,
            CodecKind::Illustrator => Self::CLSID_ILLUSTRATOR,
            CodecKind::Eps => Self::CLSID_EPS,
            CodecKind::InDesign => Self::CLSID_INDESIGN,
            CodecKind::AdobeXd => Self::CLSID_ADOBE_XD,
            CodecKind::Svg => Self::CLSID_SVG,
            CodecKind::CorelDraw => Self::CLSID_COREL_DRAW,
            CodecKind::Affinity => Self::CLSID_AFFINITY,
            CodecKind::CameraRaw => Self::CLSID_CAMERA_RAW,
        }
    }

    pub fn from_guid(guid: &GUID) -> Option<Self> {
        Self::ALL.into_iter().find(|k| &k.clsid() == guid)
    }
}

pub fn dll_directory() -> Option<PathBuf> {
    let raw = DLL_MODULE.load(Ordering::Relaxed);
    if raw != 0 {
        let hmod = HMODULE(raw as *mut core::ffi::c_void);
        let mut buf = vec![0u16; 1024];
        let len = unsafe { GetModuleFileNameW(Some(hmod), &mut buf) } as usize;
        if len > 0 && len < buf.len() {
            let path = PathBuf::from(String::from_utf16_lossy(&buf[..len]));
            if let Some(parent) = path.parent() {
                return Some(parent.to_path_buf());
            }
        }
    }

    if let Some(dir) = TEST_CODECS_DIR.get() {
        if dir.is_dir() {
            return Some(dir.clone());
        }
    }

    if cfg!(debug_assertions) {
        let candidate = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("prebuilt")
            .join("win-x64");
        if candidate.is_dir() {
            return Some(candidate);
        }
    }

    None
}

#[unsafe(no_mangle)]
pub unsafe extern "system" fn DllMain(
    hinst_dll: HMODULE,
    fdw_reason: u32,
    _lpv_reserved: *const core::ffi::c_void,
) -> BOOL {
    if fdw_reason == DLL_PROCESS_ATTACH {
        DLL_MODULE.store(hinst_dll.0 as isize, Ordering::Relaxed);
        let _ = unsafe { DisableThreadLibraryCalls(hinst_dll) };
    }
    TRUE
}

#[unsafe(no_mangle)]
pub unsafe extern "system" fn DllGetClassObject(
    rclsid: *const GUID,
    riid: *const GUID,
    ppv: *mut *mut core::ffi::c_void,
) -> HRESULT {
    if ppv.is_null() {
        return E_POINTER;
    }
    unsafe { *ppv = core::ptr::null_mut() };

    if rclsid.is_null() || riid.is_null() {
        return E_INVALIDARG;
    }

    let result = std::panic::catch_unwind(|| {
        let clsid = unsafe { *rclsid };
        let Some(kind) = CodecKind::from_guid(&clsid) else {
            return CLASS_E_CLASSNOTAVAILABLE;
        };

        let factory: IClassFactory = CodecClassFactory::new(kind).into();
        unsafe { factory.query(riid, ppv) }
    });

    result.unwrap_or(windows::Win32::Foundation::E_FAIL)
}

#[unsafe(no_mangle)]
pub unsafe extern "system" fn DllCanUnloadNow() -> HRESULT {
    if OBJECT_COUNT.load(Ordering::Relaxed) == 0 && LOCK_COUNT.load(Ordering::Relaxed) == 0 {
        S_OK
    } else {
        S_FALSE
    }
}
