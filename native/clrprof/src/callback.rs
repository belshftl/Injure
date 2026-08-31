// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::ffi::c_void;
use std::ptr::NonNull;
use std::sync::atomic::{AtomicU32, Ordering};

use crate::com::{
    E_FAIL, E_NOINTERFACE, E_POINTER, GUID, HRESULT, IID_ICorProfilerCallback, IID_IUnknown, S_OK,
    iid_eq,
};
use crate::events::{self, Event};
use crate::info::{
    COR_PRF_DISABLE_ALL_NGEN_IMAGES, COR_PRF_ENABLE_REJIT, COR_PRF_MONITOR_MODULE_LOADS,
    FunctionControl, IID_ICorProfilerInfo7, ProfilerInfo,
};
use crate::state;
use crate::vtable::{CallbackVtable, FunctionID, ModuleID, callback_vtable, mdMethodDef};
use crate::worker;

#[repr(C)]
struct Profiler {
    vtable: *const CallbackVtable,
    refs: AtomicU32,
}

static PROFILER_VTABLE: CallbackVtable = callback_vtable! {
    QueryInterface: query_interface,
    AddRef: add_ref,
    Release: release,
    Initialize: initialize,
    Shutdown: shutdown,
    ModuleLoadFinished: module_load_finished,
    ModuleUnloadStarted: module_unload_started,
    GetReJITParameters: get_rejit_parameters,
    ReJITError: rejit_error,
};

pub fn create(iid: *const GUID, out: *mut *mut c_void) -> HRESULT {
    if out.is_null() {
        return E_POINTER;
    }
    let profiler = Box::into_raw(Box::new(Profiler {
        vtable: &raw const PROFILER_VTABLE,
        refs: AtomicU32::new(0),
    }));
    unsafe { query_interface(profiler as *mut c_void, iid, out) }
}

unsafe extern "system" fn query_interface(
    this: *mut c_void,
    iid: *const GUID,
    out: *mut *mut c_void,
) -> HRESULT {
    if out.is_null() {
        return E_POINTER;
    }
    unsafe { *out = std::ptr::null_mut() };

    if !iid_eq(iid, &IID_IUnknown)
        && !IID_ICorProfilerCallback
            .iter()
            .any(|known| iid_eq(iid, known))
    {
        return E_NOINTERFACE;
    }
    unsafe {
        add_ref(this);
        *out = this;
    }
    S_OK
}

unsafe extern "system" fn add_ref(this: *mut c_void) -> u32 {
    let profiler = unsafe { &*(this as *const Profiler) };
    profiler.refs.fetch_add(1, Ordering::Relaxed) + 1
}

unsafe extern "system" fn release(this: *mut c_void) -> u32 {
    let profiler = unsafe { &*(this as *const Profiler) };
    let remaining = profiler.refs.fetch_sub(1, Ordering::AcqRel) - 1;
    if remaining == 0 {
        drop(unsafe { Box::from_raw(this as *mut Profiler) });
    }
    remaining
}

unsafe extern "system" fn initialize(_this: *mut c_void, unknown: *mut c_void) -> HRESULT {
    if unknown.is_null() {
        return E_POINTER;
    }

    let mut raw = std::ptr::null_mut();
    let query: unsafe extern "system" fn(*mut c_void, *const GUID, *mut *mut c_void) -> HRESULT =
        unsafe { std::mem::transmute(**(unknown as *const *const *const c_void)) };
    let hr = unsafe { query(unknown, &IID_ICorProfilerInfo7, &raw mut raw) };
    if hr < 0 {
        return E_FAIL;
    }
    let Some(raw) = NonNull::new(raw) else {
        return E_FAIL;
    };

    let info = ProfilerInfo(raw);
    let hr = unsafe {
        info.set_event_mask(
            COR_PRF_MONITOR_MODULE_LOADS | COR_PRF_ENABLE_REJIT | COR_PRF_DISABLE_ALL_NGEN_IMAGES,
        )
    };
    if hr < 0 {
        return hr;
    }

    state::set_info(info);
    worker::start();
    S_OK
}

unsafe extern "system" fn shutdown(_this: *mut c_void) -> HRESULT {
    events::close();
    S_OK
}

unsafe extern "system" fn module_load_finished(
    _this: *mut c_void,
    module: ModuleID,
    hr_status: HRESULT,
) -> HRESULT {
    if hr_status < 0 {
        return S_OK;
    }
    let Some(info) = state::info() else {
        return S_OK;
    };

    let path = unsafe {
        info.module_name(module)
            .map(|name| String::from_utf16_lossy(&name))
            .unwrap_or_default()
    };
    let mvid = unsafe { info.module_mvid(module).unwrap_or_default() };
    let collectible = unsafe { info.module_is_collectible(module).unwrap_or(false) };

    state::add_module(module, path, mvid, collectible);
    events::publish(Event::module_loaded(module));
    S_OK
}

unsafe extern "system" fn module_unload_started(_this: *mut c_void, module: ModuleID) -> HRESULT {
    events::publish(Event::module_unloading(module));
    state::remove_module(module);
    S_OK
}

unsafe extern "system" fn get_rejit_parameters(
    _this: *mut c_void,
    module: ModuleID,
    token: mdMethodDef,
    control: *mut c_void,
) -> HRESULT {
    let Some(control) = NonNull::new(control) else {
        return E_POINTER;
    };
    state::with_prepared(module, token, |body| unsafe {
        FunctionControl(control).set_il_function_body(body)
    })
    .unwrap_or(S_OK)
}

unsafe extern "system" fn rejit_error(
    _this: *mut c_void,
    module: ModuleID,
    token: mdMethodDef,
    _function: FunctionID,
    hr_status: HRESULT,
) -> HRESULT {
    events::publish(Event::rejit_failed(module, token, hr_status));
    S_OK
}
