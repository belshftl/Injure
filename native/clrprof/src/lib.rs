// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

#![deny(unsafe_op_in_unsafe_fn)]
#![deny(clippy::borrow_as_ptr)]
#![warn(clippy::pedantic)]
#![allow(clippy::doc_markdown)]
#![allow(clippy::ptr_as_ptr)]

mod callback;
mod com;
mod events;
mod exports;
mod info;
mod metadata;
mod state;
mod vtable;
mod worker;

use std::ffi::c_void;
use std::sync::atomic::{AtomicU32, Ordering};

use com::{
    CLASS_E_CLASSNOTAVAILABLE, CLSID_PROFILER, E_NOINTERFACE, E_POINTER, GUID, HRESULT,
    IID_IClassFactory, IID_IUnknown, S_FALSE, S_OK, iid_eq,
};

// ==============================================================================================
// class factory

#[allow(non_snake_case)]
#[repr(C)]
struct ClassFactoryVtable {
    QueryInterface:
        unsafe extern "system" fn(*mut c_void, *const GUID, *mut *mut c_void) -> HRESULT,
    AddRef: unsafe extern "system" fn(*mut c_void) -> u32,
    Release: unsafe extern "system" fn(*mut c_void) -> u32,
    CreateInstance: unsafe extern "system" fn(
        *mut c_void,
        *mut c_void,
        *const GUID,
        *mut *mut c_void,
    ) -> HRESULT,
    LockServer: unsafe extern "system" fn(*mut c_void, i32) -> HRESULT,
}

#[repr(C)]
struct ClassFactory {
    vtable: *const ClassFactoryVtable,
    refs: AtomicU32,
}

static FACTORY_VTABLE: ClassFactoryVtable = ClassFactoryVtable {
    QueryInterface: factory_query_interface,
    AddRef: factory_add_ref,
    Release: factory_release,
    CreateInstance: factory_create_instance,
    LockServer: factory_lock_server,
};

unsafe extern "system" fn factory_query_interface(
    this: *mut c_void,
    iid: *const GUID,
    out: *mut *mut c_void,
) -> HRESULT {
    if out.is_null() {
        return E_POINTER;
    }
    unsafe { *out = std::ptr::null_mut() };
    if !iid_eq(iid, &IID_IUnknown) && !iid_eq(iid, &IID_IClassFactory) {
        return E_NOINTERFACE;
    }
    unsafe {
        factory_add_ref(this);
        *out = this;
    }
    S_OK
}

unsafe extern "system" fn factory_add_ref(this: *mut c_void) -> u32 {
    let factory = unsafe { &*(this as *const ClassFactory) };
    factory.refs.fetch_add(1, Ordering::Relaxed) + 1
}

unsafe extern "system" fn factory_release(this: *mut c_void) -> u32 {
    let factory = unsafe { &*(this as *const ClassFactory) };
    let remaining = factory.refs.fetch_sub(1, Ordering::AcqRel) - 1;
    if remaining == 0 {
        drop(unsafe { Box::from_raw(this as *mut ClassFactory) });
    }
    remaining
}

unsafe extern "system" fn factory_create_instance(
    _this: *mut c_void,
    outer: *mut c_void,
    iid: *const GUID,
    out: *mut *mut c_void,
) -> HRESULT {
    if out.is_null() {
        return E_POINTER;
    }
    unsafe { *out = std::ptr::null_mut() };
    if !outer.is_null() {
        // aggregation, which a profiler is never asked for
        return E_NOINTERFACE;
    }
    callback::create(iid, out)
}

unsafe extern "system" fn factory_lock_server(_this: *mut c_void, _lock: i32) -> HRESULT {
    S_OK
}

// ==============================================================================================
// com entrypoint

/// Called by CoreCLR to obtain the profiler's class factory.
///
/// # Safety
///
/// Called by the runtime with a valid CLSID, IID, and writable out pointer.
#[unsafe(no_mangle)]
pub unsafe extern "system" fn DllGetClassObject(
    clsid: *const GUID,
    iid: *const GUID,
    out: *mut *mut c_void,
) -> HRESULT {
    if out.is_null() {
        return E_POINTER;
    }
    unsafe { *out = std::ptr::null_mut() };

    if !iid_eq(clsid, &CLSID_PROFILER) {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    let factory = Box::into_raw(Box::new(ClassFactory {
        vtable: &raw const FACTORY_VTABLE,
        refs: AtomicU32::new(0),
    }));
    unsafe { factory_query_interface(factory as *mut c_void, iid, out) }
}

/// Whether the library can be unloaded. Always `S_FALSE`, as a profiler stays for the lifetime of
/// the process, and unloading one when ReJIT'd code is still installed would be very problematic.
#[unsafe(no_mangle)]
pub extern "system" fn DllCanUnloadNow() -> HRESULT {
    S_FALSE
}
