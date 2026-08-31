// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use crate::com::{E_FAIL, E_INVALIDARG, E_POINTER, HRESULT, S_OK};
use crate::events::{self, Event};
use crate::info::{IID_IMetaDataAssemblyImport, OF_READ, OF_WRITE};
use crate::metadata::{
    IID_IMetaDataAssemblyEmit, IID_IMetaDataEmit2, IID_IMetaDataImport, ModuleMetadata,
};
use crate::state;
use crate::vtable::{ModuleID, mdMethodDef};
use crate::worker;

const E_NO_WORKER: HRESULT = E_FAIL;

fn on_worker(job: impl FnOnce() -> HRESULT + Send + 'static) -> HRESULT {
    worker::run(job).unwrap_or(E_NO_WORKER)
}

// ==============================================================================================
// attachment and events

/// Gets whether the profiler loaded and its worker is running, returning a boolean.
#[unsafe(no_mangle)]
pub extern "C" fn prof_is_attached() -> i32 {
    i32::from(state::is_attached() && worker::is_running())
}

/// Waits for profiler events and copies as many as fit, returning the amount that was written.
///
/// # Safety
///
/// `buf` must be writable for `capacity` events.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_wait_events(
    buf: *mut Event,
    capacity: usize,
    timeout_ms: u64,
) -> usize {
    unsafe { events::wait(buf, capacity, timeout_ms) }
}

/// Pushes a `Wakeup` event to the queue, for the purpose of waking a thread blocked in
/// `prof_wait_events`.
#[unsafe(no_mangle)]
pub extern "C" fn prof_wakeup() {
    events::publish(Event::wakeup());
}

// ==============================================================================================
// modules

/// Finds a loaded module by a given UTF-16 path suffix.
///
/// # Safety
///
/// `suffix` must point to `len` UTF-16 code units.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_find_module(suffix: *const u16, len: usize) -> ModuleID {
    if suffix.is_null() || len == 0 {
        return 0;
    }
    let suffix = String::from_utf16_lossy(unsafe { std::slice::from_raw_parts(suffix, len) });
    state::find_module(&suffix).unwrap_or(0)
}

/// Writes a module's path into `buf`, and the required minimum `capacity` value into `needed`.
/// `buf` may be null, in which case only `needed` will be written into.
///
/// # Safety
///
/// `buf` must be writable for `capacity` **UTF-16 code units** (not bytes) if non-null, and
/// `needed` must be writable for one `usize`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_module_path(
    module: ModuleID,
    buf: *mut u16,
    capacity: usize,
    needed: *mut usize,
) -> HRESULT {
    if needed.is_null() {
        return E_POINTER;
    }
    let Some(path) = state::module_path(module) else {
        return E_INVALIDARG;
    };
    let utf16: Vec<u16> = path.encode_utf16().collect();
    unsafe { *needed = utf16.len() };
    if buf.is_null() || capacity < utf16.len() {
        return S_OK;
    }
    unsafe { std::ptr::copy_nonoverlapping(utf16.as_ptr(), buf, utf16.len()) };
    S_OK
}

/// Writes a module's MVID into `mvid`.
///
/// # Safety
///
/// `mvid` must be writable for sixteen bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_module_mvid(module: ModuleID, mvid: *mut u8) -> HRESULT {
    if mvid.is_null() {
        return E_POINTER;
    }
    let Some(value) = state::module_mvid(module) else {
        return E_INVALIDARG;
    };
    unsafe { std::ptr::copy_nonoverlapping(value.as_ptr(), mvid, value.len()) };
    S_OK
}

/// Gets whether a module belongs to a collectible load context. Tri-state; returns either a
/// boolean or -1 if there is no much module.
#[unsafe(no_mangle)]
pub extern "C" fn prof_module_is_collectible(module: ModuleID) -> i32 {
    match state::module_is_collectible(module) {
        Some(true) => 1,
        Some(false) => 0,
        None => -1,
    }
}

// ==============================================================================================
// il

/// Writes a method's original IL into `buf`, and the required minimum `capacity` value into
/// `needed`. `buf` may be null, in which case only `needed` will be written into.
///
/// # Safety
///
/// `buf` must be writable for `capacity` bytes if non-null, and `needed` must be writable for
/// one `usize`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_get_il(
    module: ModuleID,
    token: mdMethodDef,
    buf: *mut u8,
    capacity: usize,
    needed: *mut usize,
) -> HRESULT {
    if needed.is_null() {
        return E_POINTER;
    }
    let copied = worker::run(move || {
        let info = state::info()?;
        let (header, size) = unsafe { info.il_function_body(module, token)? };
        Some(unsafe { std::slice::from_raw_parts(header, size as usize) }.to_vec())
    });
    let Some(Some(bytes)) = copied else {
        return E_FAIL;
    };

    unsafe { *needed = bytes.len() };
    if buf.is_null() || capacity < bytes.len() {
        return S_OK;
    }
    unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, bytes.len()) };
    S_OK
}

/// Stores the IL to install on a method's next ReJIT. `body` is copied out, and as such need only
/// remain valid for the duration of the call.
///
/// # Safety
///
/// `body` must be readable for `len` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_set_prepared(
    module: ModuleID,
    token: mdMethodDef,
    body: *const u8,
    len: usize,
) -> HRESULT {
    if body.is_null() || len == 0 {
        return E_POINTER;
    }
    let body = unsafe { std::slice::from_raw_parts(body, len) }.to_vec();
    state::set_prepared(module, token, body)
}

/// Requests a ReJIT for a batch of methods.
///
/// Asynchronous with respect to running code; already-executing frames continue on the old version.
///
/// # Safety
///
/// `modules` and `tokens` must each be readable for `count` **elements** (not bytes).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_request_rejit(
    modules: *const ModuleID,
    tokens: *const mdMethodDef,
    count: usize,
) -> HRESULT {
    if modules.is_null() || tokens.is_null() || count == 0 {
        return E_POINTER;
    }
    let modules = unsafe { std::slice::from_raw_parts(modules, count) }.to_vec();
    let tokens = unsafe { std::slice::from_raw_parts(tokens, count) }.to_vec();
    on_worker(move || match state::info() {
        Some(info) => unsafe { info.request_rejit(&modules, &tokens) },
        None => E_FAIL,
    })
}

/// Reverts a batch of methods to the IL in their modules and forgets their prepared bodies.
///
/// # Safety
///
/// `modules` and `tokens` must each be readable for `count` **elements** (not bytes).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_request_revert(
    modules: *const ModuleID,
    tokens: *const mdMethodDef,
    count: usize,
) -> HRESULT {
    if modules.is_null() || tokens.is_null() || count == 0 {
        return E_POINTER;
    }
    let modules = unsafe { std::slice::from_raw_parts(modules, count) }.to_vec();
    let tokens = unsafe { std::slice::from_raw_parts(tokens, count) }.to_vec();

    for (module, token) in modules.iter().zip(tokens.iter()) {
        state::clear_prepared(*module, *token);
    }
    on_worker(move || match state::info() {
        Some(info) => {
            let mut status = vec![S_OK; modules.len()];
            let hr = unsafe { info.request_revert(&modules, &tokens, &mut status) };
            if hr < 0 {
                hr
            } else {
                status.into_iter().find(|val| *val < 0).unwrap_or(S_OK)
            }
        }
        None => E_FAIL,
    })
}

// ==============================================================================================
// metadata

enum MetadataOpenError {
    NoSuchModule,
    NoMetadata,
    Rejected(HRESULT),
}

impl From<MetadataOpenError> for HRESULT {
    fn from(error: MetadataOpenError) -> HRESULT {
        match error {
            MetadataOpenError::NoSuchModule => E_INVALIDARG,
            MetadataOpenError::NoMetadata => E_FAIL,
            MetadataOpenError::Rejected(hr) => hr,
        }
    }
}

fn require(
    result: Result<Option<std::ptr::NonNull<std::ffi::c_void>>, HRESULT>,
) -> Result<std::ptr::NonNull<std::ffi::c_void>, MetadataOpenError> {
    match result {
        Ok(Some(ptr)) => Ok(ptr),
        Ok(None) => Err(MetadataOpenError::NoMetadata),
        Err(hr) => Err(MetadataOpenError::Rejected(hr)),
    }
}

fn with_metadata<T>(
    module: ModuleID,
    f: impl FnOnce(&ModuleMetadata) -> T,
) -> Result<T, MetadataOpenError> {
    let info = state::info().ok_or(MetadataOpenError::NoSuchModule)?;
    let mut guard = state::state().lock().unwrap();
    let entry = guard
        .modules
        .get_mut(&module)
        .ok_or(MetadataOpenError::NoSuchModule)?;

    if entry.metadata.is_none() {
        entry.metadata = Some(state::ModuleMetadata {
            emit: require(unsafe {
                info.module_metadata_hr(module, OF_WRITE, &IID_IMetaDataEmit2)
            })?,
            assembly_emit: require(unsafe {
                info.module_metadata_hr(module, OF_WRITE, &IID_IMetaDataAssemblyEmit)
            })?,
            import: require(unsafe {
                info.module_metadata_hr(module, OF_READ, &IID_IMetaDataImport)
            })?,
            assembly_import: require(unsafe {
                info.module_metadata_hr(module, OF_READ, &IID_IMetaDataAssemblyImport)
            })?,
        });
    }

    let cached = entry
        .metadata
        .as_ref()
        .expect("should've been just inserted or already present");
    Ok(f(&ModuleMetadata {
        emit: cached.emit,
        assembly_emit: cached.assembly_emit,
    }))
}

unsafe fn emit_with(
    module: ModuleID,
    token: *mut u32,
    job: impl FnOnce(&ModuleMetadata) -> Result<u32, HRESULT> + Send + 'static,
) -> HRESULT {
    if token.is_null() {
        return E_POINTER;
    }
    let outcome = worker::run(move || match with_metadata(module, job) {
        Ok(result) => result,
        Err(open_err) => Err(HRESULT::from(open_err)),
    });
    match outcome {
        Some(Ok(value)) => {
            unsafe { *token = value };
            S_OK
        }
        Some(Err(hr)) => hr,
        None => E_NO_WORKER,
    }
}

/// Defines an `AssemblyRef`, or returns the existing row for the same identity.
///
/// # Safety
///
/// `name` must be readable for `name_len` **UTF-16 code units** (not bytes), and `token` must be
/// writable for one `u32`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_define_assembly_ref(
    module: ModuleID,
    name: *const u16,
    name_len: usize,
    major: u16,
    minor: u16,
    build: u16,
    revision: u16,
    public_key: *const u8,
    public_key_len: usize,
    flags: u32,
    token: *mut u32,
) -> HRESULT {
    if name.is_null() || name_len == 0 {
        return E_POINTER;
    }
    let name = String::from_utf16_lossy(unsafe { std::slice::from_raw_parts(name, name_len) });
    let key = if public_key.is_null() || public_key_len == 0 {
        Vec::new()
    } else {
        unsafe { std::slice::from_raw_parts(public_key, public_key_len) }.to_vec()
    };
    unsafe {
        emit_with(module, token, move |md| {
            md.assembly_ref(&name, (major, minor, build, revision), &key, flags)
        })
    }
}

/// Defines a `TypeRef`, or returns the existing row.
///
/// # Safety
///
/// `namespace` must be readable for `namespace_len` **UTF-16 code units** (not bytes), `name` must
/// be readable for `name_len` **UTF-16 code units** (not bytes), and `token` must be writable for
/// one `u32`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_define_type_ref(
    module: ModuleID,
    scope: u32,
    namespace: *const u16,
    namespace_len: usize,
    name: *const u16,
    name_len: usize,
    token: *mut u32,
) -> HRESULT {
    if name.is_null() || name_len == 0 {
        return E_POINTER;
    }
    let namespace = if namespace.is_null() || namespace_len == 0 {
        String::new()
    } else {
        String::from_utf16_lossy(unsafe { std::slice::from_raw_parts(namespace, namespace_len) })
    };
    let name = String::from_utf16_lossy(unsafe { std::slice::from_raw_parts(name, name_len) });
    unsafe {
        emit_with(module, token, move |md| {
            md.type_ref(scope, &namespace, &name)
        })
    }
}

/// Defines a `MemberRef`, or returns the existing row.
///
/// # Safety
///
/// `name` must be readable for `name_len` **UTF-16 code units** (not bytes), `signature` must be
/// readable for `signature_len` bytes, and `token` must be writable for one `u32`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_define_member_ref(
    module: ModuleID,
    parent: u32,
    name: *const u16,
    name_len: usize,
    signature: *const u8,
    signature_len: usize,
    token: *mut u32,
) -> HRESULT {
    if name.is_null() || signature.is_null() || name_len == 0 || signature_len == 0 {
        return E_POINTER;
    }
    let name = String::from_utf16_lossy(unsafe { std::slice::from_raw_parts(name, name_len) });
    let signature = unsafe { std::slice::from_raw_parts(signature, signature_len) }.to_vec();
    unsafe {
        emit_with(module, token, move |md| {
            md.member_ref(parent, &name, &signature)
        })
    }
}

/// Defines a `TypeSpec` from an encoded signature.
///
/// # Safety
///
/// `signature` must be readable for `signature_len` bytes, and `token` must be writable for one
/// `u32`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_define_type_spec(
    module: ModuleID,
    signature: *const u8,
    signature_len: usize,
    token: *mut u32,
) -> HRESULT {
    if signature.is_null() || signature_len == 0 {
        return E_POINTER;
    }
    let signature = unsafe { std::slice::from_raw_parts(signature, signature_len) }.to_vec();
    unsafe { emit_with(module, token, move |md| md.type_spec(&signature)) }
}

/// Defines a `MethodSpec` over a method and its generic arguments.
///
/// # Safety
///
/// `signature` must be readable for `signature_len` bytes, and `token` must be writable for one
/// `u32`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_define_method_spec(
    module: ModuleID,
    method: u32,
    signature: *const u8,
    signature_len: usize,
    token: *mut u32,
) -> HRESULT {
    if signature.is_null() || signature_len == 0 {
        return E_POINTER;
    }
    let blob = unsafe { std::slice::from_raw_parts(signature, signature_len) }.to_vec();
    unsafe { emit_with(module, token, move |md| md.method_spec(method, &blob)) }
}

/// Defines a `StandAloneSig`, which `calli` and locals signatures need.
///
/// # Safety
///
/// `signature` must be readable for `signature_len` bytes, and `token` must be writable for one
/// `u32`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_define_standalone_sig(
    module: ModuleID,
    signature: *const u8,
    signature_len: usize,
    token: *mut u32,
) -> HRESULT {
    if signature.is_null() || signature_len == 0 {
        return E_POINTER;
    }
    let blob = unsafe { std::slice::from_raw_parts(signature, signature_len) }.to_vec();
    unsafe { emit_with(module, token, move |md| md.standalone_sig(&blob)) }
}

/// Adds a user string, returning its token (prefixed with `0x70`).
///
/// # Safety
///
/// `value` must be readable for `len` **UTF-16 code units** (not bytes), and `token` must be
/// writable for one `u32`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn prof_define_user_string(
    module: ModuleID,
    value: *const u16,
    len: usize,
    token: *mut u32,
) -> HRESULT {
    if value.is_null() {
        return E_POINTER;
    }
    let text = String::from_utf16_lossy(unsafe { std::slice::from_raw_parts(value, len) });
    unsafe { emit_with(module, token, move |md| md.user_string(&text)) }
}

/// Publishes rows defined since the last call to this function.
///
/// Tokens handed out by the other functions are usable in IL only after this returns.
#[unsafe(no_mangle)]
pub extern "C" fn prof_apply_metadata(module: ModuleID) -> HRESULT {
    on_worker(move || match state::info() {
        Some(info) => unsafe { info.apply_metadata(module) },
        None => E_FAIL,
    })
}
