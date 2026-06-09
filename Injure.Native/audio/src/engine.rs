// SPDX-License-Identifier: MIT

use core::f64::consts::TAU;
use rtrb::{Consumer, Producer, RingBuffer};
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, AtomicI32, AtomicI64, Ordering};

use crate::commands::{AE_COMMAND_SET_TEST_TONE, AeCommand};

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AeConfig {
    pub channels: i32,
    pub command_capacity: i32,
    pub max_voices: i32,
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct AeStats {
    pub frame_cursor: i64,
    pub sample_rate: i32,
    pub quantum_frames: i32,
    pub channel_count: i32,
    pub xrun_count: i64,
    pub running: i32,
    pub cpu_load: f32,
    pub dropped_command_count: i64,
}

pub enum EnqueueError {
    Full,
    LockPoisoned,
}

pub struct ControlState {
    pub channels: i32,
    pub sample_rate: AtomicI32,
    pub quantum_frames: AtomicI32,
    pub frame_cursor: AtomicI64,
    pub xrun_count: AtomicI64,
    pub running: AtomicBool,
    pub dropped_command_count: AtomicI64,
    command_producer: Mutex<Producer<AeCommand>>,
}

impl ControlState {
    fn new(config: &AeConfig, producer: Producer<AeCommand>) -> Self {
        Self {
            channels: if config.channels <= 0 {
                2
            } else {
                config.channels
            },
            sample_rate: AtomicI32::new(0),
            quantum_frames: AtomicI32::new(0),
            frame_cursor: AtomicI64::new(0),
            xrun_count: AtomicI64::new(0),
            running: AtomicBool::new(false),
            dropped_command_count: AtomicI64::new(0),
            command_producer: Mutex::new(producer),
        }
    }

    pub fn enqueue_command(&self, command: AeCommand) -> Result<(), EnqueueError> {
        let Ok(mut producer) = self.command_producer.lock() else {
            return Err(EnqueueError::LockPoisoned);
        };
        producer.push(command).map_err(|_| EnqueueError::Full)
    }

    pub fn fill_stats(&self, out: &mut AeStats, cpu_load: f32) {
        out.frame_cursor = self.frame_cursor.load(Ordering::Acquire);
        out.sample_rate = self.sample_rate.load(Ordering::Acquire);
        out.quantum_frames = self.quantum_frames.load(Ordering::Acquire);
        out.channel_count = self.channels;
        out.xrun_count = self.xrun_count.load(Ordering::Acquire);
        out.running = i32::from(self.running.load(Ordering::Acquire));
        out.cpu_load = cpu_load;
        out.dropped_command_count = self.dropped_command_count.load(Ordering::Acquire);
    }
}

pub struct ToneState {
    enabled: bool,
    hz: f32,
    gain: f32,
    phase: f64,
}

pub struct RtState {
    frame_cursor: i64,
    command_consumer: Consumer<AeCommand>,
    tone: ToneState,
}

impl RtState {
    fn new(command_consumer: Consumer<AeCommand>) -> Self {
        Self {
            frame_cursor: 0,
            command_consumer,
            tone: ToneState {
                enabled: false,
                hz: 440.0,
                gain: 0.1,
                phase: 0.0,
            },
        }
    }

    pub fn process_stereo(&mut self, control: &ControlState, left: &mut [f32], right: &mut [f32]) {
        left.fill(0.0);
        right.fill(0.0);

        let nframes = std::cmp::min(left.len(), right.len());

        while let Ok(command) = self.command_consumer.pop() {
            self.apply_command(command);
        }

        let sr = control.sample_rate.load(Ordering::Relaxed);
        if sr > 0 && self.tone.enabled {
            let step = self.tone.hz as f64 / sr as f64;

            for i in 0..nframes {
                let sample = (self.tone.phase * TAU).sin() as f32 * self.tone.gain;

                self.tone.phase += step;
                if self.tone.phase >= 1.0 {
                    self.tone.phase -= self.tone.phase.floor();
                }

                left[i] = sample;
                right[i] = sample;
            }
        }

        self.frame_cursor += nframes as i64;
        control
            .frame_cursor
            .store(self.frame_cursor, Ordering::Release);
    }

    fn apply_command(&mut self, command: AeCommand) {
        #[allow(clippy::single_match)]
        match command.kind {
            AE_COMMAND_SET_TEST_TONE => {
                // SAFETY: this union field selected by `command.kind`
                let cmd = unsafe { command.data.set_test_tone };

                self.tone.enabled = cmd.enabled != 0;
                self.tone.hz = cmd.hz;
                self.tone.gain = cmd.gain;

                if !self.tone.enabled {
                    self.tone.phase = 0.0;
                }
            }
            _ => {}
        }
    }
}

pub struct Engine {
    pub control: Box<ControlState>,
    pub rt: Box<RtState>,
}

impl Engine {
    pub fn new(config: &AeConfig) -> Self {
        let capacity = config.command_capacity.max(16) as usize;
        let (producer, consumer) = RingBuffer::<AeCommand>::new(capacity);

        Self {
            control: Box::new(ControlState::new(config, producer)),
            rt: Box::new(RtState::new(consumer)),
        }
    }

    pub fn enqueue_command(&self, command: AeCommand) -> Result<(), EnqueueError> {
        self.control.enqueue_command(command)
    }

    pub fn fill_stats(&self, out: &mut AeStats, cpu_load: f32) {
        self.control.fill_stats(out, cpu_load);
    }
}
