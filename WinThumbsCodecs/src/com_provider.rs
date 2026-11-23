use std::sync::Mutex;
use std::sync::atomic::Ordering;
use windows::Win32::Foundation::{
    CLASS_E_NOAGGREGATION, E_FAIL, E_INVALIDARG, E_POINTER, E_UNEXPECTED, HINSTANCE,
};
use windows::Win32::Graphics::Gdi::HBITMAP;
use windows::Win32::System::Com::{IClassFactory, IClassFactory_Impl, IStream};
use windows::Win32::UI::Shell::PropertiesSystem::{
    IInitializeWithStream, IInitializeWithStream_Impl,
};
use windows::Win32::UI::Shell::{IThumbnailProvider, IThumbnailProvider_Impl, WTS_ALPHATYPE};
use windows::core::{BOOL, GUID, IUnknown, Interface, Ref, Result as WinResult, implement};

use crate::{CodecKind, LOCK_COUNT, OBJECT_COUNT, bitmap, decoders};

#[implement(IClassFactory)]
pub struct CodecClassFactory {
    kind: CodecKind,
}

impl CodecClassFactory {
    pub fn new(kind: CodecKind) -> Self {
        OBJECT_COUNT.fetch_add(1, Ordering::Relaxed);
        Self { kind }
    }
}

impl Drop for CodecClassFactory {
    fn drop(&mut self) {
        crate::dec_saturating(&OBJECT_COUNT);
    }
}

impl IClassFactory_Impl for CodecClassFactory_Impl {
    fn CreateInstance(
        &self,
        punkouter: Ref<'_, IUnknown>,
        riid: *const GUID,
        ppvobject: *mut *mut core::ffi::c_void,
    ) -> WinResult<()> {
        if ppvobject.is_null() {
            return Err(E_POINTER.into());
        }
        unsafe { *ppvobject = core::ptr::null_mut() };

        if riid.is_null() {
            return Err(E_INVALIDARG.into());
        }
        if punkouter.is_some() {
            return Err(CLASS_E_NOAGGREGATION.into());
        }

        let provider: IThumbnailProvider = ThumbnailProvider::new(self.kind).into();
        unsafe { provider.query(riid, ppvobject) }.ok()
    }

    fn LockServer(&self, flock: BOOL) -> WinResult<()> {
        if flock.as_bool() {
            LOCK_COUNT.fetch_add(1, Ordering::Relaxed);
        } else {
            crate::dec_saturating(&LOCK_COUNT);
        }
        Ok(())
    }
}

#[implement(IThumbnailProvider, IInitializeWithStream)]
pub struct ThumbnailProvider {
    kind: CodecKind,
    stream: Mutex<Option<IStream>>,
}

impl ThumbnailProvider {
    pub fn new(kind: CodecKind) -> Self {
        OBJECT_COUNT.fetch_add(1, Ordering::Relaxed);
        Self {
            kind,
            stream: Mutex::new(None),
        }
    }
}

impl Drop for ThumbnailProvider {
    fn drop(&mut self) {
        crate::dec_saturating(&OBJECT_COUNT);
    }
}

impl IInitializeWithStream_Impl for ThumbnailProvider_Impl {
    fn Initialize(&self, pstream: Ref<'_, IStream>, _grfmode: u32) -> WinResult<()> {
        let stream = pstream.ok()?.clone();
        let mut guard = self.stream.lock().map_err(|_| E_FAIL)?;
        if guard.is_some() {
            return Err(E_UNEXPECTED.into());
        }
        *guard = Some(stream);
        Ok(())
    }
}

impl IThumbnailProvider_Impl for ThumbnailProvider_Impl {
    fn GetThumbnail(
        &self,
        cx: u32,
        phbmp: *mut HBITMAP,
        pdwalpha: *mut WTS_ALPHATYPE,
    ) -> WinResult<()> {
        if phbmp.is_null() || pdwalpha.is_null() {
            return Err(E_POINTER.into());
        }
        unsafe {
            *phbmp = HBITMAP::default();
            *pdwalpha = Default::default();
        }

        let stream = {
            let guard = self.stream.lock().map_err(|_| E_FAIL)?;
            guard.as_ref().ok_or(E_UNEXPECTED)?.clone()
        };

        let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            let decoded = decoders::decode_from_stream(self.kind, &stream, cx)?;
            let fitted = bitmap::resize_fit(decoded, cx)?;
            bitmap::to_hbitmap(&fitted)
        }));

        match result {
            Ok(Ok((hbmp, alpha_type))) => {
                unsafe {
                    *phbmp = hbmp;
                    *pdwalpha = alpha_type;
                }
                Ok(())
            }
            Ok(Err(_)) | Err(_) => Err(E_FAIL.into()),
        }
    }
}

#[allow(dead_code)]
fn _ensure_hinstance_type(_: HINSTANCE) {}
