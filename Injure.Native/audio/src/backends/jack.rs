// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use libc::{c_char, c_int, c_void};
use std::ffi::{CStr, CString};
use std::ptr;
use std::sync::atomic::Ordering;

use crate::AeResult;
use crate::engine::{ControlState, RtState};

mod ffi {
    use super::*;

    #[repr(C)]
    pub struct jack_client_t {
        _private: [u8; 0],
    }

    #[repr(C)]
    pub struct jack_port_t {
        _private: [u8; 0],
    }

    pub type JackNFrames = u32;
    pub type JackOptions = u32;
    pub type JackStatus = u32;

    pub const JACK_NULL_OPTION: JackOptions = 0;
    pub const JACK_PORT_IS_INPUT: u64 = 0x1;
    pub const JACK_PORT_IS_OUTPUT: u64 = 0x2;
    pub const JACK_PORT_IS_PHYSICAL: u64 = 0x4;
    pub const JACK_DEFAULT_AUDIO_TYPE: &CStr = c"32 bit float mono audio";

    unsafe extern "C" {
        pub fn jack_client_open(
            client_name: *const c_char,
            options: JackOptions,
            status: *mut JackStatus,
            ...
        ) -> *mut jack_client_t;

        pub fn jack_client_close(client: *mut jack_client_t) -> c_int;

        pub fn jack_activate(client: *mut jack_client_t) -> c_int;
        pub fn jack_deactivate(client: *mut jack_client_t) -> c_int;

        pub fn jack_get_sample_rate(client: *mut jack_client_t) -> JackNFrames;
        pub fn jack_get_buffer_size(client: *mut jack_client_t) -> JackNFrames;
        pub fn jack_cpu_load(client: *mut jack_client_t) -> f32;

        pub fn jack_set_process_callback(
            client: *mut jack_client_t,
            process_callback: Option<unsafe extern "C" fn(JackNFrames, *mut c_void) -> c_int>,
            arg: *mut c_void,
        ) -> c_int;

        pub fn jack_set_sample_rate_callback(
            client: *mut jack_client_t,
            sample_rate_callback: Option<unsafe extern "C" fn(JackNFrames, *mut c_void) -> c_int>,
            arg: *mut c_void,
        ) -> c_int;

        pub fn jack_set_buffer_size_callback(
            client: *mut jack_client_t,
            buffer_size_callback: Option<unsafe extern "C" fn(JackNFrames, *mut c_void) -> c_int>,
            arg: *mut c_void,
        ) -> c_int;

        pub fn jack_set_xrun_callback(
            client: *mut jack_client_t,
            xrun_callback: Option<unsafe extern "C" fn(*mut c_void) -> c_int>,
            arg: *mut c_void,
        ) -> c_int;

        pub fn jack_on_shutdown(
            client: *mut jack_client_t,
            shutdown_callback: Option<unsafe extern "C" fn(*mut c_void)>,
            arg: *mut c_void,
        );

        pub fn jack_port_register(
            client: *mut jack_client_t,
            port_name: *const c_char,
            port_type: *const c_char,
            flags: u64,
            buffer_size: u64,
        ) -> *mut jack_port_t;

        pub fn jack_port_get_buffer(port: *mut jack_port_t, nframes: JackNFrames) -> *mut c_void;

        pub fn jack_port_name(port: *const jack_port_t) -> *const c_char;

        pub fn jack_get_ports(
            client: *mut jack_client_t,
            port_name_pattern: *const c_char,
            type_name_pattern: *const c_char,
            flags: u64,
        ) -> *mut *const c_char;

        pub fn jack_connect(
            client: *mut jack_client_t,
            source_port: *const c_char,
            destination_port: *const c_char,
        ) -> c_int;

        pub fn jack_free(ptr: *mut c_void);
    }
}

use self::ffi::*;

pub struct JackContext {
    control: *const ControlState,
    rt: *mut RtState,
    ports: Vec<*mut jack_port_t>,
}

pub struct JackBackend {
    client: *mut jack_client_t,
    context: Box<JackContext>,
}

impl JackBackend {
    pub unsafe fn start(
        control: *const ControlState,
        rt: *mut RtState,
        client_name: &CStr,
        channels: u32,
        autoconnect: bool,
    ) -> Result<Self, AeResult> {
        if control.is_null() || rt.is_null() || channels == 0 {
            return Err(AeResult::Null);
        }

        let mut status: JackStatus = 0;
        let client =
            unsafe { jack_client_open(client_name.as_ptr(), JACK_NULL_OPTION, &raw mut status) };

        if client.is_null() {
            return Err(AeResult::JackOpenFailed);
        }

        // SAFETY: `client` is live, and `control` and `rt` are valid non-null pointers
        // owned by `Engine` that must outlive this `JackBackend`
        let result = unsafe { Self::finish_start(client, control, rt, channels, autoconnect) };

        if result.is_err() {
            unsafe { jack_client_close(client) };
        }

        result
    }

    unsafe fn finish_start(
        client: *mut jack_client_t,
        control: *const ControlState,
        rt: *mut RtState,
        channels: u32,
        autoconnect: bool,
    ) -> Result<Self, AeResult> {
        let sr = unsafe { jack_get_sample_rate(client) };
        let bs = unsafe { jack_get_buffer_size(client) };

        // SAFETY: this function is called from `start`, which checks that `control` is valid
        unsafe {
            (*control).sample_rate.store(sr, Ordering::Release);
            (*control).quantum_frames.store(bs, Ordering::Release);
        }

        let mut context = Box::new(JackContext {
            control,
            rt,
            ports: Vec::with_capacity(channels as usize),
        });

        for i in 0..channels {
            let Ok(name) = CString::new(format!("out_{}", i + 1)) else {
                return Err(AeResult::JackPortFailed);
            };

            let port = unsafe {
                jack_port_register(
                    client,
                    name.as_ptr(),
                    JACK_DEFAULT_AUDIO_TYPE.as_ptr(),
                    JACK_PORT_IS_OUTPUT,
                    0,
                )
            };

            if port.is_null() {
                return Err(AeResult::JackPortFailed);
            }

            context.ports.push(port);
        }

        let arg = (&raw mut *context).cast::<c_void>();
        if unsafe { jack_set_process_callback(client, Some(process_callback), arg) } != 0 {
            return Err(AeResult::BadState);
        }
        unsafe {
            jack_set_sample_rate_callback(client, Some(sample_rate_callback), arg);
            jack_set_buffer_size_callback(client, Some(buffer_size_callback), arg);
            jack_set_xrun_callback(client, Some(xrun_callback), arg);
            jack_on_shutdown(client, Some(shutdown_callback), arg);
        }

        if unsafe { jack_activate(client) } != 0 {
            return Err(AeResult::JackActivateFailed);
        }

        // SAFETY: this function is called from `start`, which checks that `control` is valid
        unsafe { (*control).running.store(true, Ordering::Release) };

        let backend = Self { client, context };
        if autoconnect {
            backend.autoconnect_physical_outputs();
        }
        Ok(backend)
    }

    pub fn stop(&mut self) {
        if !self.client.is_null() {
            unsafe {
                jack_deactivate(self.client);
                jack_client_close(self.client);
            }
            self.client = ptr::null_mut();
        }
        if !self.context.control.is_null() {
            unsafe {
                (*self.context.control)
                    .running
                    .store(false, Ordering::Release);
            };
        }
    }

    pub fn cpu_load(&self) -> f32 {
        if self.client.is_null() {
            0.0
        } else {
            unsafe { jack_cpu_load(self.client) }
        }
    }

    fn autoconnect_physical_outputs(&self) {
        if self.client.is_null() {
            return;
        }

        let ports = unsafe {
            jack_get_ports(
                self.client,
                ptr::null(),
                JACK_DEFAULT_AUDIO_TYPE.as_ptr(),
                JACK_PORT_IS_PHYSICAL | JACK_PORT_IS_INPUT,
            )
        };
        if ports.is_null() {
            return;
        }

        for i in 0..self.context.ports.len() {
            let dst = unsafe { *ports.add(i) };
            if dst.is_null() {
                break;
            }

            let src = unsafe { jack_port_name(self.context.ports[i]) };
            if !src.is_null() {
                _ = unsafe { jack_connect(self.client, src, dst) };
            }
        }

        unsafe { jack_free(ports.cast()) };
    }
}

impl Drop for JackBackend {
    fn drop(&mut self) {
        self.stop();
    }
}

unsafe extern "C" fn process_callback(nframes: JackNFrames, arg: *mut c_void) -> c_int {
    let Some(ctx) = jack_ctx_from_arg(arg) else {
        return 0;
    };

    if ctx.ports.len() < 2 || ctx.control.is_null() || ctx.rt.is_null() {
        return 0;
    }

    let left_ptr = unsafe { jack_port_get_buffer(ctx.ports[0], nframes).cast::<f32>() };
    let right_ptr = unsafe { jack_port_get_buffer(ctx.ports[1], nframes).cast::<f32>() };

    if left_ptr.is_null() || right_ptr.is_null() {
        return 0;
    }

    let n = nframes as usize;

    // SAFETY: JACK guarantees that an output port buffer returned during the process callback is
    // writable for `nframes` samples
    let left = unsafe { core::slice::from_raw_parts_mut(left_ptr, n) };
    let right = unsafe { core::slice::from_raw_parts_mut(right_ptr, n) };

    // SAFETY: `ctx.control` points to `Engine`'s `control`, which outlives the `JackBackend`
    let control = unsafe { &*ctx.control };

    // SAFETY: the JACK process callback is the only thread that mutates the `RtState` while this
    // backend is active
    let rt = unsafe { &mut *ctx.rt };
    rt.process_stereo(control, left, right);
    0
}

unsafe extern "C" fn sample_rate_callback(nframes: JackNFrames, arg: *mut c_void) -> c_int {
    let Some(ctx) = jack_ctx_from_arg(arg) else {
        return 0;
    };

    if !ctx.control.is_null() {
        // SAFETY: `ctx.control` points to `Engine`'s `control`, which outlives the `JackBackend`
        unsafe {
            (*ctx.control)
                .sample_rate
                .store(nframes, Ordering::Release);
        };
    }

    0
}

unsafe extern "C" fn buffer_size_callback(nframes: JackNFrames, arg: *mut c_void) -> c_int {
    let Some(ctx) = jack_ctx_from_arg(arg) else {
        return 0;
    };

    if !ctx.control.is_null() {
        // SAFETY: `ctx.control` points to `Engine`'s `control`, which outlives the `JackBackend`
        unsafe {
            (*ctx.control)
                .quantum_frames
                .store(nframes, Ordering::Release);
        };
    }

    0
}

unsafe extern "C" fn xrun_callback(arg: *mut c_void) -> c_int {
    let Some(ctx) = jack_ctx_from_arg(arg) else {
        return 0;
    };

    if !ctx.control.is_null() {
        // SAFETY: `ctx.control` points to `Engine`'s `control`, which outlives the `JackBackend`
        unsafe { (*ctx.control).xrun_count.fetch_add(1, Ordering::Release) };
    }

    0
}

unsafe extern "C" fn shutdown_callback(arg: *mut c_void) {
    let Some(ctx) = jack_ctx_from_arg(arg) else {
        return;
    };

    if !ctx.control.is_null() {
        unsafe {
            // SAFETY: `ctx.control` points to `Engine`'s `control`, which outlives the `JackBackend`
            (*ctx.control).running.store(false, Ordering::Release);
        }
    }
}

fn jack_ctx_from_arg<'a>(arg: *mut c_void) -> Option<&'a mut JackContext> {
    if arg.is_null() {
        return None;
    }

    // SAFETY: this is the same pointer registered during the callback registration; `JackBackend`
    // owns it and closes/deactivates JACK before dropping it
    Some(unsafe { &mut *arg.cast::<JackContext>() })
}
