// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::collections::HashMap;
use std::ffi::c_void;
use std::ptr::NonNull;
use std::sync::{Mutex, OnceLock};

use crate::com::{E_INVALIDARG, HRESULT, S_OK};
use crate::info::ProfilerInfo;
use crate::metadata::release;
use crate::vtable::{ModuleID, mdMethodDef};

pub struct Module {
    pub path: String,
    pub mvid: [u8; 16],
    pub collectible: bool,
    pub metadata: Option<ModuleMetadata>,
}

pub struct ModuleMetadata {
    pub emit: NonNull<c_void>,
    pub assembly_emit: NonNull<c_void>,
    pub import: NonNull<c_void>,
    pub assembly_import: NonNull<c_void>,
}

#[derive(Default)]
pub struct State {
    pub modules: HashMap<ModuleID, Module>,
    pub prepared: HashMap<(ModuleID, mdMethodDef), Vec<u8>>,
}

// SAFETY: the metadata pointers inside are only ever dereferenced on the worker thread
unsafe impl Send for State {}

static STATE: OnceLock<Mutex<State>> = OnceLock::new();
static INFO: OnceLock<ProfilerInfo> = OnceLock::new();

pub fn state() -> &'static Mutex<State> {
    STATE.get_or_init(|| Mutex::new(State::default()))
}

pub fn info() -> Option<ProfilerInfo> {
    INFO.get().copied()
}

pub fn set_info(value: ProfilerInfo) {
    _ = INFO.set(value);
}

pub fn is_attached() -> bool {
    INFO.get().is_some()
}

pub fn add_module(id: ModuleID, path: String, mvid: [u8; 16], collectible: bool) {
    state().lock().unwrap().modules.insert(
        id,
        Module {
            path,
            mvid,
            collectible,
            metadata: None,
        },
    );
}

pub fn remove_module(id: ModuleID) {
    let mut state = state().lock().unwrap();
    if let Some(module) = state.modules.remove(&id)
        && let Some(metadata) = module.metadata
    {
        // release here rather than in a `Drop` impl, since this must happen on the thread that
        // opened them, and a `Drop` would run wherever the map happened to be mutated
        unsafe {
            release(metadata.emit);
            release(metadata.assembly_emit);
            release(metadata.import);
            release(metadata.assembly_import);
        }
    }
    state.prepared.retain(|(module, _), _| *module != id);
}

pub fn find_module(suffix: &str) -> Option<ModuleID> {
    state()
        .lock()
        .unwrap()
        .modules
        .iter()
        .find(|(_, module)| module.path.ends_with(suffix))
        .map(|(id, _)| *id)
}

pub fn module_path(id: ModuleID) -> Option<String> {
    state()
        .lock()
        .unwrap()
        .modules
        .get(&id)
        .map(|module| module.path.clone())
}

pub fn module_mvid(id: ModuleID) -> Option<[u8; 16]> {
    state()
        .lock()
        .unwrap()
        .modules
        .get(&id)
        .map(|module| module.mvid)
}

pub fn module_is_collectible(id: ModuleID) -> Option<bool> {
    state()
        .lock()
        .unwrap()
        .modules
        .get(&id)
        .map(|module| module.collectible)
}

pub fn set_prepared(module: ModuleID, token: mdMethodDef, body: Vec<u8>) -> HRESULT {
    let mut state = state().lock().unwrap();
    if !state.modules.contains_key(&module) {
        return E_INVALIDARG;
    }
    state.prepared.insert((module, token), body);
    S_OK
}

pub fn clear_prepared(module: ModuleID, token: mdMethodDef) {
    state().lock().unwrap().prepared.remove(&(module, token));
}

pub fn with_prepared<T>(
    module: ModuleID,
    token: mdMethodDef,
    f: impl FnOnce(&[u8]) -> T,
) -> Option<T> {
    state()
        .lock()
        .unwrap()
        .prepared
        .get(&(module, token))
        .map(|body| f(body))
}
