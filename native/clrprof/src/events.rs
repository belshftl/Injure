// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::collections::VecDeque;
use std::sync::{Condvar, Mutex, OnceLock};
use std::time::Duration;

use crate::com::HRESULT;
use crate::vtable::{ModuleID, mdMethodDef};

#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EventKind {
    ModuleLoaded = 1,
    ModuleUnloading = 2,
    ReJitFailed = 3,
    Shutdown = 4,
    Wakeup = 5,
}

#[repr(C)]
#[derive(Debug, Clone)]
pub struct Event {
    pub kind: EventKind,
    // [+ 4 bytes of padding here on targets with 64 bit wide pointers]
    pub module: ModuleID,
    pub token: mdMethodDef,
    pub hresult: HRESULT,
} // aligned to 8 bytes on 64 bit and 4 bytes on 32 bit

#[cfg(target_pointer_width = "64")]
const _: () = {
    // + 4 bytes (kind)
    // + 4 bytes of padding
    // + 8 bytes (module)
    // + 4 bytes (token)
    // + 4 bytes (hresult)
    // = 24

    assert!(size_of::<Event>() == 24);

    assert!(std::mem::align_of::<Event>() == 8);

    assert!(std::mem::offset_of!(Event, kind) == 0);
    assert!(std::mem::offset_of!(Event, module) == 8);
    assert!(std::mem::offset_of!(Event, token) == 16);
    assert!(std::mem::offset_of!(Event, hresult) == 20);
};

#[cfg(target_pointer_width = "32")]
const _: () = {
    // + 4 bytes (kind)
    // + 4 bytes (module)
    // + 4 bytes (token)
    // + 4 bytes (hresult)
    // = 16

    assert!(size_of::<Event>() == 16);

    assert!(std::mem::align_of::<Event>() == 4);

    assert!(std::mem::offset_of!(Event, kind) == 0);
    assert!(std::mem::offset_of!(Event, module) == 4);
    assert!(std::mem::offset_of!(Event, token) == 8);
    assert!(std::mem::offset_of!(Event, hresult) == 12);
};

impl Event {
    pub const fn module_loaded(module: ModuleID) -> Self {
        Self {
            kind: EventKind::ModuleLoaded,
            module,
            token: 0,
            hresult: 0,
        }
    }

    pub const fn module_unloading(module: ModuleID) -> Self {
        Self {
            kind: EventKind::ModuleUnloading,
            module,
            token: 0,
            hresult: 0,
        }
    }

    pub const fn rejit_failed(module: ModuleID, token: mdMethodDef, hresult: HRESULT) -> Self {
        Self {
            kind: EventKind::ReJitFailed,
            module,
            token,
            hresult,
        }
    }

    pub const fn shutdown() -> Self {
        Self {
            kind: EventKind::Shutdown,
            module: 0,
            token: 0,
            hresult: 0,
        }
    }

    pub const fn wakeup() -> Self {
        Self {
            kind: EventKind::Wakeup,
            module: 0,
            token: 0,
            hresult: 0,
        }
    }
}

struct Queue {
    pending: Mutex<State>,
    ready: Condvar,
}

#[derive(Default)]
struct State {
    events: VecDeque<Event>,
    closed: bool,
}

static QUEUE: OnceLock<Queue> = OnceLock::new();

fn queue() -> &'static Queue {
    QUEUE.get_or_init(|| Queue {
        pending: Mutex::new(State::default()),
        ready: Condvar::new(),
    })
}

pub fn publish(event: Event) {
    let queue = queue();
    let mut state = queue.pending.lock().unwrap();
    if state.closed {
        return;
    }
    state.events.push_back(event);
    drop(state);
    queue.ready.notify_one();
}

pub unsafe fn wait(buffer: *mut Event, capacity: usize, timeout_ms: u64) -> usize {
    if buffer.is_null() || capacity == 0 {
        return 0;
    }
    let queue = queue();
    let mut state = queue.pending.lock().unwrap();

    if state.events.is_empty() && !state.closed {
        let timeout = Duration::from_millis(timeout_ms);
        (state, _) = queue
            .ready
            .wait_timeout_while(state, timeout, |s| s.events.is_empty() && !s.closed)
            .unwrap();
    }

    let n = state.events.len().min(capacity);
    for i in 0..n {
        let ev = state.events.pop_front().expect("length was just checked");
        unsafe { buffer.add(i).write(ev) };
    }
    n
}

pub fn close() {
    publish(Event::shutdown());
    let queue = queue();
    queue.pending.lock().unwrap().closed = true;
    queue.ready.notify_all();
}
