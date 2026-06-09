// SPDX-License-Identifier: MIT

#![deny(unsafe_op_in_unsafe_fn)]
#![deny(clippy::borrow_as_ptr)]
#![allow(dead_code)]
#![allow(clippy::missing_safety_doc)]

mod backends;
mod commands;
mod engine;

use libc::c_char;
use std::ffi::CStr;

use backends::jack::JackBackend;
use commands::{AeCommand, AeResult};
use engine::{AeConfig, AeStats, Engine};

#[repr(C)]
pub struct ae_engine {
    engine: Box<Engine>,
    jack: Option<JackBackend>,
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_create(config: *const AeConfig) -> *mut ae_engine {
    let default_config = AeConfig {
        channels: 2,
        command_capacity: 1024,
        max_voices: 256,
    };

    let cfg = if config.is_null() {
        &default_config
    } else {
        // SAFETY: `config` has been checked non-null, caller promises that it points to a valid
        // `AeConfig` for the duration of this call
        unsafe { &*config }
    };

    let wrapper = ae_engine {
        engine: Box::new(Engine::new(cfg)),
        jack: None,
    };

    Box::into_raw(Box::new(wrapper))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_destroy(engine: *mut ae_engine) {
    if engine.is_null() {
        return;
    }

    // SAFETY: `engine` has been checked non-null, caller promises that it has been
    // returned by `ae_create` and that `ae_destroy` has not been called on it before
    let mut boxed = unsafe { Box::from_raw(engine) };

    if let Some(mut jack) = boxed.jack.take() {
        jack.stop()
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_start_jack(
    wrapper: *mut ae_engine,
    client_name: *const c_char,
    autoconnect: i32,
) -> i32 {
    if wrapper.is_null() || client_name.is_null() {
        return AeResult::Null as i32;
    }

    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live
    // `ae_engine` that's not concurrently being destroyed
    let wrapper = unsafe { &mut *wrapper };
    if wrapper.jack.is_some() {
        return AeResult::BadState as i32;
    }

    let name = unsafe { CStr::from_ptr(client_name) };
    let control_ptr = &raw const *wrapper.engine.control;
    let rt_ptr = &raw mut *wrapper.engine.rt;

    // SAFETY: `control_ptr` and `rt_ptr` both point to boxed state owned by the engine and remain
    // stable while the backend is stored in `wrapper.jack`
    match unsafe {
        JackBackend::start(
            control_ptr,
            rt_ptr,
            name,
            wrapper.engine.control.channels,
            autoconnect != 0,
        )
    } {
        Ok(jack) => {
            wrapper.jack = Some(jack);
            AeResult::Ok as i32
        }
        Err(e) => e as i32,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_stop(wrapper: *mut ae_engine) -> i32 {
    if wrapper.is_null() {
        return AeResult::Null as i32;
    }

    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live
    // `ae_engine` that's not concurrently being destroyed
    let wrapper = unsafe { &mut *wrapper };
    if let Some(mut jack) = wrapper.jack.take() {
        jack.stop()
    }

    AeResult::Ok as i32
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_get_stats(wrapper: *const ae_engine, out_stats: *mut AeStats) -> i32 {
    if wrapper.is_null() || out_stats.is_null() {
        return AeResult::Null as i32;
    }

    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live
    // `ae_engine` that's not concurrently being destroyed
    let wrapper = unsafe { &*wrapper };

    let cpu = wrapper.jack.as_ref().map(|j| j.cpu_load()).unwrap_or(0.0);

    // SAFETY: `out_stats` has been checked non-null, caller promises it points to
    // a writable `AeStats`
    wrapper.engine.fill_stats(unsafe { &mut *out_stats }, cpu);

    AeResult::Ok as i32
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ae_enqueue_command(
    wrapper: *mut ae_engine,
    command: *const AeCommand,
) -> i32 {
    if wrapper.is_null() || command.is_null() {
        return AeResult::Null as i32;
    }

    // SAFETY: `wrapper` has been checked non-null, caller promises that it's a live
    // `ae_engine` that's not concurrently being destroyed
    let wrapper = unsafe { &*wrapper };

    // SAFETY: `command` was checked non-null and is copied out immediately
    let command = unsafe { *command };
    if !command.is_semantically_valid() {
        return AeResult::BadCommand as i32;
    }

    match wrapper.engine.enqueue_command(command) {
        Ok(()) => AeResult::Ok as i32,
        Err(e) => match e {
            engine::EnqueueError::Full => AeResult::CommandQueueFull as i32,
            engine::EnqueueError::LockPoisoned => AeResult::LockPoisoned as i32,
        },
    }
}
