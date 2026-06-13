// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

#![deny(unsafe_op_in_unsafe_fn)]
#![deny(clippy::borrow_as_ptr)]
#![warn(clippy::pedantic)]
#![allow(clippy::missing_safety_doc)]
#![allow(clippy::wildcard_imports)]

mod assets;
mod backends;
mod commands;
mod engine;
mod voices;

use assets::{AeSoundDesc, AeSoundId, AeSoundMapping};
use backends::jack::JackBackend;
use commands::{AePlayVoiceDesc, AeResult, AeVoiceId};
use engine::{AeConfig, AeStats, Engine};
use libc::c_char;
use std::ffi::CStr;

#[repr(C)]
pub struct AeEngineWrapper {
    engine: Box<Engine>,
    jack: Option<JackBackend>,
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_create(config: *const AeConfig) -> *mut AeEngineWrapper {
    let default_config = AeConfig {
        channels: 2,
        command_capacity: 1024,
        maintenance_capacity: 1024,
    };

    let config = if config.is_null() {
        &default_config
    } else {
        // SAFETY: `config` has been checked non-null, caller promises that it's a valid `AeConfig`
        unsafe { &*config }
    };

    Box::into_raw(Box::new(AeEngineWrapper {
        engine: Box::new(Engine::new(config)),
        jack: None,
    }))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_destroy(wrapper: *mut AeEngineWrapper) {
    if wrapper.is_null() {
        return;
    }

    // SAFETY: `wrapper` has been checked non-null, caller promises that it has been
    // returned by `ae_create` and that `ae_destroy` has not been called on it before
    let mut boxed = unsafe { Box::from_raw(wrapper) };
    if let Some(mut jack) = boxed.jack.take() {
        jack.stop();
    }
    _ = boxed.engine.collect_garbage();
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_start_jack(
    wrapper: *mut AeEngineWrapper,
    client_name: *const c_char,
    autoconnect: i32,
) -> AeResult {
    if wrapper.is_null() || client_name.is_null() {
        return AeResult::Null;
    }

    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed
    let wrapper = unsafe { &mut *wrapper };
    if wrapper.jack.is_some() {
        return AeResult::BadState;
    }

    // SAFETY: `client_name` has been checked non-null, caller promises it's a valid C string
    let name = unsafe { CStr::from_ptr(client_name) };
    let control = std::ptr::from_ref(&*wrapper.engine.control);
    let rt = std::ptr::from_mut(&mut *wrapper.engine.rt);
    // SAFETY: `control` and `rt` both point to boxed state owned by the engine and remain
    // stable while the backend is stored in `wrapper.jack`
    match unsafe {
        JackBackend::start(
            control,
            rt,
            name,
            wrapper.engine.control.channels,
            autoconnect != 0,
        )
    } {
        Ok(jack) => {
            wrapper.jack = Some(jack);
            AeResult::Ok
        }
        Err(e) => e,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_stop(wrapper: *mut AeEngineWrapper) -> AeResult {
    if wrapper.is_null() {
        return AeResult::Null;
    }
    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed
    let wrapper = unsafe { &mut *wrapper };
    if let Some(mut jack) = wrapper.jack.take() {
        jack.stop();
    }
    match wrapper.engine.collect_garbage() {
        Ok(()) => AeResult::Ok,
        Err(e) => e,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_get_stats(
    wrapper: *mut AeEngineWrapper,
    out_stats: *mut AeStats,
) -> AeResult {
    if wrapper.is_null() || out_stats.is_null() {
        return AeResult::Null;
    }

    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed
    let wrapper = unsafe { &*wrapper };

    // SAFETY: `out_stats` has been checked non-null, caller promises that it's writable for
    // `AeStats`
    let out_stats = unsafe { &mut *out_stats };

    let cpu_load = wrapper.jack.as_ref().map_or(0.0, JackBackend::cpu_load);
    match wrapper.engine.fill_stats(out_stats, cpu_load) {
        Ok(()) => AeResult::Ok,
        Err(e) => e,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_collect_garbage(wrapper: *mut AeEngineWrapper) -> AeResult {
    if wrapper.is_null() {
        AeResult::Null
    } else {
        // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live
        // `ae_engine` that's not concurrently being destroyed
        match unsafe { &*wrapper }.engine.collect_garbage() {
            Ok(()) => AeResult::Ok,
            Err(e) => e,
        }
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_sound_alloc(
    wrapper: *mut AeEngineWrapper,
    desc: *const AeSoundDesc,
    out_sound: *mut AeSoundId,
) -> AeResult {
    if wrapper.is_null() || desc.is_null() || out_sound.is_null() {
        return AeResult::Null;
    }
    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed; `desc` has been checked non-null, caller
    // promises that it's a valid `AeSoundDesc`
    match unsafe { &*wrapper }.engine.sound_alloc(unsafe { &*desc }) {
        Ok(id) => {
            // SAFETY: `out_sound` has been checked non-null, caller promises that it's writable for
            // `AeSoundId`
            unsafe { *out_sound = id };
            AeResult::Ok
        }
        Err(e) => e,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_sound_get_buffer(
    wrapper: *mut AeEngineWrapper,
    sound: AeSoundId,
    out_mapping: *mut AeSoundMapping,
) -> AeResult {
    if wrapper.is_null() || out_mapping.is_null() {
        return AeResult::Null;
    }
    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed
    match unsafe { &*wrapper }.engine.sound_mapping(sound) {
        Ok(mapping) => {
            // SAFETY: `out_mapping` has been checked non-null, caller promises that it's writable
            // for `AeSoundMapping`
            unsafe { *out_mapping = mapping };
            AeResult::Ok
        }
        Err(e) => e,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_sound_commit(
    wrapper: *mut AeEngineWrapper,
    sound: AeSoundId,
) -> AeResult {
    if wrapper.is_null() {
        return AeResult::Null;
    }
    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed
    match unsafe { &*wrapper }.engine.sound_commit(sound) {
        Ok(()) => AeResult::Ok,
        Err(e) => e,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_sound_free(
    wrapper: *mut AeEngineWrapper,
    sound: AeSoundId,
) -> AeResult {
    if wrapper.is_null() {
        return AeResult::Null;
    }
    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed
    match unsafe { &*wrapper }.engine.sound_free(sound) {
        Ok(()) => AeResult::Ok,
        Err(e) => e,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_voice_play(
    wrapper: *mut AeEngineWrapper,
    desc: *const AePlayVoiceDesc,
    out_voice: *mut AeVoiceId,
) -> AeResult {
    if wrapper.is_null() || desc.is_null() || out_voice.is_null() {
        return AeResult::Null;
    }
    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed; `desc` has been checked non-null, caller
    // promises that it's a valid `AePlayVoiceDesc`
    match unsafe { &*wrapper }.engine.voice_play(unsafe { &*desc }) {
        Ok(id) => {
            unsafe { *out_voice = id };
            AeResult::Ok
        }
        Err(e) => e,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_voice_stop(
    wrapper: *mut AeEngineWrapper,
    voice: AeVoiceId,
) -> AeResult {
    if wrapper.is_null() {
        return AeResult::Null;
    }
    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live `ae_engine`
    // that's not concurrently being destroyed
    match unsafe { &*wrapper }.engine.voice_stop(voice) {
        Ok(()) => AeResult::Ok,
        Err(e) => e,
    }
}
