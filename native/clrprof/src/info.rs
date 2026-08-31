// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::ffi::c_void;
use std::ptr::NonNull;

use crate::com::{E_POINTER, GUID, HRESULT, count};
use crate::vtable::{ModuleID, call_slot, mdMethodDef};

const SLOT_SET_EVENT_MASK: usize = 16;
const SLOT_GET_MODULE_METADATA: usize = 21;
const SLOT_GET_IL_FUNCTION_BODY: usize = 22;
const SLOT_GET_MODULE_INFO2: usize = 70;
const SLOT_REQUEST_REJIT: usize = 73;
const SLOT_REQUEST_REVERT: usize = 74;
const SLOT_APPLY_METADATA: usize = 84;

const COR_PRF_MODULE_COLLECTIBLE: u32 = 0x0000_0008;

const SLOT_FC_SET_IL_FUNCTION_BODY: usize = 4;

pub const COR_PRF_MONITOR_MODULE_LOADS: u32 = 0x0000_0004;
pub const COR_PRF_ENABLE_REJIT: u32 = 0x0004_0000;
pub const COR_PRF_DISABLE_ALL_NGEN_IMAGES: u32 = 0x8000_0000;

#[allow(non_upper_case_globals)]
pub const IID_ICorProfilerInfo7: GUID = GUID::new(
    0x9aee_cc0d,
    0x63e0,
    0x4187,
    [0x8c, 0x00, 0xe3, 0x12, 0xf5, 0x03, 0xf6, 0x63],
);

pub const OF_READ: u32 = 0x0000_0000;
pub const OF_WRITE: u32 = 0x0000_0001;

#[allow(non_upper_case_globals)]
pub const IID_IMetaDataAssemblyImport: GUID = GUID::new(
    0xee62_470b,
    0xe94b,
    0x424e,
    [0x9b, 0x7c, 0x2f, 0x00, 0xc9, 0x24, 0x9f, 0x93],
);

#[derive(Clone, Copy)]
pub struct ProfilerInfo(pub NonNull<c_void>);

// SAFETY: ICorProfilerInfo is free-threaded, the runtime documents it as callable from any thread
unsafe impl Send for ProfilerInfo {}
unsafe impl Sync for ProfilerInfo {}

impl ProfilerInfo {
    pub unsafe fn set_event_mask(self, mask: u32) -> HRESULT {
        call_slot!(
            self.0.as_ptr(),
            SLOT_SET_EVENT_MASK,
            fn(u32) -> HRESULT,
            (mask)
        )
    }

    unsafe fn module_info2(self, module: ModuleID) -> Option<(Vec<u16>, u32)> {
        let mut needed = 0;
        let mut flags = 0;
        let hr = call_slot!(
            self.0.as_ptr(),
            SLOT_GET_MODULE_INFO2,
            fn(ModuleID, *mut *const u8, u32, *mut u32, *mut u16, *mut usize, *mut u32) -> HRESULT,
            (
                module,
                std::ptr::null_mut(),
                0,
                &raw mut needed,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                &raw mut flags,
            )
        );
        if hr < 0 {
            return None;
        }
        if needed == 0 {
            // module with no file
            return Some((Vec::new(), flags));
        }

        let mut name = vec![0u16; needed as usize];
        let mut written = 0;
        let hr = call_slot!(
            self.0.as_ptr(),
            SLOT_GET_MODULE_INFO2,
            fn(ModuleID, *mut *const u8, u32, *mut u32, *mut u16, *mut usize, *mut u32) -> HRESULT,
            (
                module,
                std::ptr::null_mut(),
                needed,
                &raw mut written,
                name.as_mut_ptr(),
                std::ptr::null_mut(),
                &raw mut flags,
            )
        );
        if hr < 0 {
            return None;
        }
        name.truncate(written.saturating_sub(1) as usize);
        Some((name, flags))
    }

    pub unsafe fn module_name(self, module: ModuleID) -> Option<Vec<u16>> {
        unsafe { self.module_info2(module).map(|(name, _)| name) }
    }

    pub unsafe fn module_is_collectible(self, module: ModuleID) -> Option<bool> {
        unsafe {
            self.module_info2(module)
                .map(|(_, flags)| flags & COR_PRF_MODULE_COLLECTIBLE != 0)
        }
    }

    pub unsafe fn module_mvid(self, module: ModuleID) -> Option<[u8; 16]> {
        let import = unsafe {
            self.module_metadata_hr(module, OF_READ, &crate::metadata::IID_IMetaDataImport)
                .ok()?? // `.ok()?` discards a rejected call, the second `?` discards "no metadata at all"
        };
        let mvid = unsafe { crate::metadata::scope_mvid(import) };
        unsafe { crate::metadata::release(import) };
        mvid
    }

    pub unsafe fn il_function_body(
        self,
        module: ModuleID,
        token: mdMethodDef,
    ) -> Option<(*const u8, u32)> {
        let mut header = std::ptr::null();
        let mut size = 0;
        let hr = call_slot!(
            self.0.as_ptr(),
            SLOT_GET_IL_FUNCTION_BODY,
            fn(ModuleID, mdMethodDef, *mut *const u8, *mut u32) -> HRESULT,
            (module, token, &raw mut header, &raw mut size)
        );
        if hr < 0 || header.is_null() {
            None
        } else {
            Some((header, size))
        }
    }

    pub unsafe fn module_metadata_hr(
        self,
        module: ModuleID,
        flags: u32,
        iid: &GUID,
    ) -> Result<Option<NonNull<c_void>>, HRESULT> {
        let mut out = std::ptr::null_mut();
        let hr = call_slot!(
            self.0.as_ptr(),
            SLOT_GET_MODULE_METADATA,
            fn(ModuleID, u32, *const GUID, *mut *mut c_void) -> HRESULT,
            (module, flags, iid, &raw mut out)
        );
        if hr < 0 {
            Err(hr)
        } else {
            Ok(NonNull::new(out))
        }
    }

    pub unsafe fn apply_metadata(self, module: ModuleID) -> HRESULT {
        call_slot!(
            self.0.as_ptr(),
            SLOT_APPLY_METADATA,
            fn(ModuleID) -> HRESULT,
            (module)
        )
    }

    pub unsafe fn request_rejit(self, modules: &[ModuleID], tokens: &[mdMethodDef]) -> HRESULT {
        debug_assert_eq!(modules.len(), tokens.len());
        call_slot!(
            self.0.as_ptr(),
            SLOT_REQUEST_REJIT,
            fn(u32, *const ModuleID, *const mdMethodDef) -> HRESULT,
            (count(modules.len()), modules.as_ptr(), tokens.as_ptr())
        )
    }

    pub unsafe fn request_revert(
        self,
        modules: &[ModuleID],
        tokens: &[mdMethodDef],
        status: &mut [HRESULT],
    ) -> HRESULT {
        debug_assert_eq!(modules.len(), tokens.len());
        debug_assert_eq!(modules.len(), status.len());
        call_slot!(
            self.0.as_ptr(),
            SLOT_REQUEST_REVERT,
            fn(u32, *const ModuleID, *const mdMethodDef, *mut HRESULT) -> HRESULT,
            (
                count(modules.len()),
                modules.as_ptr(),
                tokens.as_ptr(),
                status.as_mut_ptr(),
            )
        )
    }
}

#[derive(Clone, Copy)]
pub struct FunctionControl(pub NonNull<c_void>);

impl FunctionControl {
    pub unsafe fn set_il_function_body(self, body: &[u8]) -> HRESULT {
        if body.is_empty() {
            return E_POINTER;
        }
        call_slot!(
            self.0.as_ptr(),
            SLOT_FC_SET_IL_FUNCTION_BODY,
            fn(u32, *const u8) -> HRESULT,
            (count(body.len()), body.as_ptr())
        )
    }
}
