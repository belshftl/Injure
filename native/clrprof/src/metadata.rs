// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::ffi::c_void;
use std::ptr::NonNull;

use crate::com::{GUID, HRESULT, count};
use crate::vtable::{call_slot, slot};

const SLOT_DEFINE_TYPE_REF_BY_NAME: usize = 12;
const SLOT_DEFINE_MEMBER_REF: usize = 14;
const SLOT_GET_TOKEN_FROM_SIG: usize = 23;
const SLOT_GET_TOKEN_FROM_TYPE_SPEC: usize = 26;
const SLOT_DEFINE_USER_STRING: usize = 28;
const SLOT_DEFINE_METHOD_SPEC: usize = 52;

const SLOT_DEFINE_ASSEMBLY_REF: usize = 4;

const SLOT_GET_SCOPE_PROPS: usize = 10;

#[allow(non_upper_case_globals)]
pub const IID_IMetaDataEmit2: GUID = GUID::new(
    0xf5dd_9950,
    0xf693,
    0x42e6,
    [0x83, 0x0e, 0x7b, 0x83, 0x3e, 0x81, 0x46, 0xa9],
);

#[allow(non_upper_case_globals)]
pub const IID_IMetaDataAssemblyEmit: GUID = GUID::new(
    0x211e_f15b,
    0x5317,
    0x4438,
    [0xb1, 0x96, 0xde, 0xc8, 0x7b, 0x88, 0x76, 0x93],
);

#[allow(non_upper_case_globals)]
pub const IID_IMetaDataImport: GUID = GUID::new(
    0x7dac_8207,
    0xd3ae,
    0x4c75,
    [0x9b, 0x67, 0x92, 0x80, 0x1a, 0x49, 0x7d, 0x44],
);

#[derive(Default)]
#[repr(C)]
pub struct AssemblyMetadata {
    pub major: u16,
    pub minor: u16,
    pub build: u16,
    pub revision: u16,
    pub locale: *mut u16,
    pub locale_len: u32,
    pub processors: *mut u32,
    pub processor_count: u32,
    pub os: *mut c_void,
    pub os_count: u32,
}

pub fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

/// # Safety
///
/// `this` must be a live interface pointer the caller owns a reference to.
pub unsafe fn release(this: NonNull<c_void>) {
    let f: unsafe extern "system" fn(*mut c_void) -> u32 =
        unsafe { std::mem::transmute(slot(this.as_ptr(), 2)) };
    unsafe { f(this.as_ptr()) };
}

pub struct ModuleMetadata {
    pub emit: NonNull<c_void>,
    pub assembly_emit: NonNull<c_void>,
}

impl ModuleMetadata {
    pub unsafe fn type_ref(&self, scope: u32, namespace: &str, name: &str) -> Result<u32, HRESULT> {
        let full = if namespace.is_empty() {
            name.to_owned()
        } else {
            format!("{namespace}.{name}")
        };
        let wide_name = wide(&full);

        let mut token = 0;
        let hr = call_slot!(
            self.emit.as_ptr(),
            SLOT_DEFINE_TYPE_REF_BY_NAME,
            fn(u32, *const u16, *mut u32) -> HRESULT,
            (scope, wide_name.as_ptr(), &raw mut token)
        );
        if hr < 0 { Err(hr) } else { Ok(token) }
    }

    pub unsafe fn member_ref(
        &self,
        parent: u32,
        name: &str,
        signature: &[u8],
    ) -> Result<u32, HRESULT> {
        let wide_name = wide(name);

        let mut token = 0;
        let hr = call_slot!(
            self.emit.as_ptr(),
            SLOT_DEFINE_MEMBER_REF,
            fn(u32, *const u16, *const u8, u32, *mut u32) -> HRESULT,
            (
                parent,
                wide_name.as_ptr(),
                signature.as_ptr(),
                count(signature.len()),
                &raw mut token,
            )
        );
        if hr < 0 { Err(hr) } else { Ok(token) }
    }

    pub unsafe fn method_spec(&self, parent: u32, signature: &[u8]) -> Result<u32, HRESULT> {
        let mut token = 0;
        let hr = call_slot!(
            self.emit.as_ptr(),
            SLOT_DEFINE_METHOD_SPEC,
            fn(u32, *const u8, u32, *mut u32) -> HRESULT,
            (
                parent,
                signature.as_ptr(),
                count(signature.len()),
                &raw mut token
            )
        );
        if hr < 0 { Err(hr) } else { Ok(token) }
    }

    pub unsafe fn type_spec(&self, signature: &[u8]) -> Result<u32, HRESULT> {
        let mut token = 0;
        let hr = call_slot!(
            self.emit.as_ptr(),
            SLOT_GET_TOKEN_FROM_TYPE_SPEC,
            fn(*const u8, u32, *mut u32) -> HRESULT,
            (signature.as_ptr(), count(signature.len()), &raw mut token)
        );
        if hr < 0 { Err(hr) } else { Ok(token) }
    }

    pub unsafe fn standalone_sig(&self, signature: &[u8]) -> Result<u32, HRESULT> {
        let mut token = 0;
        let hr = call_slot!(
            self.emit.as_ptr(),
            SLOT_GET_TOKEN_FROM_SIG,
            fn(*const u8, u32, *mut u32) -> HRESULT,
            (signature.as_ptr(), count(signature.len()), &raw mut token)
        );
        if hr < 0 { Err(hr) } else { Ok(token) }
    }

    pub unsafe fn user_string(&self, text: &str) -> Result<u32, HRESULT> {
        let utf16: Vec<u16> = text.encode_utf16().collect();
        let mut token = 0;
        let hr = call_slot!(
            self.emit.as_ptr(),
            SLOT_DEFINE_USER_STRING,
            fn(*const u16, u32, *mut u32) -> HRESULT,
            (utf16.as_ptr(), count(utf16.len()), &raw mut token)
        );
        if hr < 0 { Err(hr) } else { Ok(token) }
    }

    pub unsafe fn assembly_ref(
        &self,
        name: &str,
        version: (u16, u16, u16, u16),
        public_key_token: &[u8],
        flags: u32,
    ) -> Result<u32, HRESULT> {
        let wide_name = wide(name);
        let metadata = AssemblyMetadata {
            major: version.0,
            minor: version.1,
            build: version.2,
            revision: version.3,
            ..Default::default()
        };

        let mut token = 0;
        let hr = call_slot!(
            self.assembly_emit.as_ptr(),
            SLOT_DEFINE_ASSEMBLY_REF,
            fn(
                *const u8,
                u32,
                *const u16,
                *const AssemblyMetadata,
                *const u8,
                u32,
                u32,
                *mut u32,
            ) -> HRESULT,
            (
                if public_key_token.is_empty() {
                    std::ptr::null()
                } else {
                    public_key_token.as_ptr()
                },
                count(public_key_token.len()),
                wide_name.as_ptr(),
                &raw const metadata,
                std::ptr::null(),
                0,
                flags,
                &raw mut token,
            )
        );
        if hr < 0 { Err(hr) } else { Ok(token) }
    }
}

pub unsafe fn scope_mvid(import: NonNull<c_void>) -> Option<[u8; 16]> {
    let mut mvid = [0u8; 16];
    let mut written = 0;
    let hr = call_slot!(
        import.as_ptr(),
        SLOT_GET_SCOPE_PROPS,
        fn(*mut u16, u32, *mut u32, *mut u8) -> HRESULT,
        (std::ptr::null_mut(), 0, &raw mut written, mvid.as_mut_ptr())
    );
    if hr < 0 { None } else { Some(mvid) }
}
