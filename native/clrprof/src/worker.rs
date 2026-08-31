// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::sync::{
    Mutex, OnceLock,
    mpsc::{Sender, channel},
};

type Job = Box<dyn FnOnce() + Send>;

static QUEUE: OnceLock<Mutex<Sender<Job>>> = OnceLock::new();

pub fn start() {
    if QUEUE.get().is_some() {
        return;
    }
    let (tx, rx) = channel();
    if QUEUE.set(Mutex::new(tx)).is_err() {
        return;
    }
    std::thread::Builder::new()
        .name("profiler-worker".to_owned())
        .spawn(move || {
            while let Ok(job) = rx.recv() {
                job();
            }
        })
        .expect("failed to start profiler worker thread");
}

pub fn is_running() -> bool {
    QUEUE.get().is_some()
}

pub fn run<T: Send + 'static>(job: impl FnOnce() -> T + Send + 'static) -> Option<T> {
    let queue = QUEUE.get()?;
    let (tx, rx) = channel();
    queue
        .lock()
        .unwrap()
        .send(Box::new(move || {
            _ = tx.send(job());
        }))
        .ok()?;
    rx.recv().ok()
}
