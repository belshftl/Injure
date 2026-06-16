// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use rtrb::{Consumer, Producer, RingBuffer};
use std::num::NonZero;
use std::ptr::NonNull;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::sync::Mutex;

use crate::AeResult;
use crate::assets::{AeSoundDesc, AeSoundId, AeSoundMapping, SoundTable};
use crate::ring::{MaintenanceEvent, RtCommand};
use crate::voices::{ActiveVoice, AE_VOICE_FLAG_LOOP, AePlayVoiceDesc, AeVoiceId, VOICES_PER_BLOCK, VoiceBlock, VoicePool};

#[repr(transparent)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct AeOptionalMixFrame(pub Option<NonZero<u64>>);

impl AeOptionalMixFrame {
    pub const IMMEDIATE: Self = Self(None);

    pub fn from_mix_frame(frame: MixFrame) -> Self {
        Self(Some(frame.0))
    }

    pub fn to_mix_frame(self) -> Option<MixFrame> {
        self.0.map(MixFrame)
    }
}

#[repr(transparent)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct MixFrame(NonZero<u64>);

impl MixFrame {
    pub const FIRST: Self = Self(NonZero::<u64>::MIN);

    pub fn new(value: u64) -> Option<Self> {
        NonZero::<u64>::new(value).map(Self)
    }

    pub fn get(self) -> u64 {
        self.0.get()
    }

    pub fn checked_add_frames(self, frames: u64) -> Option<Self> {
        let value = self.get().checked_add(frames)?;
        NonZero::<u64>::new(value).map(Self)
    }
}

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AeConfig {
    pub channels: u32,
    pub command_capacity: usize,
    pub maintenance_capacity: usize,
}

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AeStats {
    pub mix_frame: MixFrame,
    pub sample_rate: u32,
    pub quantum_frames: u32,
    pub channel_count: u32,
    pub xrun_count: u64,
    pub running: i32,
    pub cpu_load: f32,
    pub active_voice_count: u64,
    pub voice_block_count: u64,
    pub failed_voice_start_count: u64,
    pub allocated_sound_count: u64,
    pub committed_sound_count: u64,
    pub rt_error: AeResult,
}

struct ProducerState {
    producer: Producer<RtCommand>,
    next_voice_id: Option<AeVoiceId>,
    available_voice_credits: usize,
}

pub struct ControlState {
    pub channels: u32,
    pub sample_rate: AtomicU32,
    pub quantum_frames: AtomicU32,
    pub xrun_count: AtomicU64,
    pub running: AtomicBool,
    pub active_voice_count: AtomicU64,
    pub voice_block_count: AtomicU64,
    pub failed_voice_start_count: AtomicU64,
    mix_frame: AtomicU64,
    producer: Mutex<ProducerState>,
    maintenance_consumer: Mutex<Consumer<MaintenanceEvent>>,
    sounds: Mutex<SoundTable>,
    allocated_sound_count: AtomicU64,
    committed_sound_count: AtomicU64,
    rt_error: AtomicU32,
}

impl ControlState {
    pub fn acquire_mix_frame(&self) -> MixFrame {
        let raw = self.mix_frame.load(Ordering::Acquire);
        MixFrame::new(raw).expect("ControlState.mix_frame must be nonzero")
    }

    pub fn publish_mix_frame(&self, frame: MixFrame) {
        self.mix_frame.store(frame.get(), Ordering::Release);
    }

    pub fn acquire_rt_error(&self) -> AeResult {
        let raw = self.rt_error.load(Ordering::Acquire);
        AeResult::try_from(raw).expect("ControlState.rt_error must contain a valid AeResult numeric value")
    }

    pub fn publish_rt_error(&self, error: AeResult) {
        debug_assert_ne!(error, AeResult::Ok);
        if error == AeResult::Ok {
            return;
        }
        _ = self.rt_error.compare_exchange(AeResult::Ok as u32, error as u32, Ordering::Release, Ordering::Relaxed);
    }

    #[inline]
    pub fn has_rt_error(&self) -> bool {
        self.rt_error.load(Ordering::Acquire) != AeResult::Ok as u32
    }
}

pub struct RtState {
    mix_frame: MixFrame,
    command_consumer: Consumer<RtCommand>,
    maintenance_producer: Producer<MaintenanceEvent>,
    voices: VoicePool,
}

pub struct Engine {
    pub control: Box<ControlState>,
    pub rt: Box<RtState>,
}

impl Engine {
    pub fn try_new(config: &AeConfig) -> Result<Self, AeResult> {
        if config.channels == 0 || config.command_capacity == 0 || config.maintenance_capacity == 0 {
            return Err(AeResult::Null);
        }

        let command_capacity = config.command_capacity;
        let maintenance_capacity = config.maintenance_capacity;
        let (command_producer, command_consumer) = RingBuffer::new(command_capacity);
        let (maintenance_producer, maintenance_consumer) = RingBuffer::new(maintenance_capacity);

        Ok(Self {
            control: Box::new(ControlState {
                channels: config.channels,
                sample_rate: AtomicU32::new(0),
                quantum_frames: AtomicU32::new(0),
                xrun_count: AtomicU64::new(0),
                running: AtomicBool::new(false),
                active_voice_count: AtomicU64::new(0),
                voice_block_count: AtomicU64::new(0),
                failed_voice_start_count: AtomicU64::new(0),
                mix_frame: AtomicU64::new(1), // again, 0 is not a valid value
                producer: Mutex::new(ProducerState {
                    producer: command_producer,
                    next_voice_id: Some(AeVoiceId(1)),
                    available_voice_credits: 0,
                }),
                maintenance_consumer: Mutex::new(maintenance_consumer),
                sounds: Mutex::new(SoundTable::new()),
                allocated_sound_count: AtomicU64::new(0),
                committed_sound_count: AtomicU64::new(0),
                rt_error: AtomicU32::new(AeResult::Ok as u32),
            }),
            rt: Box::new(RtState {
                mix_frame: MixFrame::FIRST,
                command_consumer,
                maintenance_producer,
                voices: VoicePool::new(),
            }),
        })
    }

    pub fn collect_garbage(&self) -> Result<(), AeResult> {
        let mut producer = self
            .control
            .producer
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?;
        let mut maintenance = self
            .control
            .maintenance_consumer
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?;

        while let Ok(event) = maintenance.pop() {
            match event {
                MaintenanceEvent::VoiceSlotReleased => {
                    producer.available_voice_credits =
                        producer.available_voice_credits.saturating_add(1);
                }
                MaintenanceEvent::ReclaimVoiceBlock {
                    block,
                    slots,
                } => {
                    producer.available_voice_credits = producer
                        .available_voice_credits
                        .saturating_sub(slots);
                    // SAFETY: RT thread removed this block from its linked list and transferred
                    // exclusive ownership through the maintenance ring
                    unsafe { drop(Box::from_raw(block.as_ptr())) };
                }
            }
        }

        Ok(())
    }

    pub fn sound_alloc(&self, desc: &AeSoundDesc) -> Result<AeSoundId, AeResult> {
        self.check_fatal_err()?;
        self.collect_garbage()?;
        let mut sounds = self.control.sounds.lock().map_err(|_| AeResult::MutexPoisoned)?;
        let id = sounds.allocate(desc)?;
        let (allocated, committed) = sounds.counts();
        self.control.allocated_sound_count.store(allocated, Ordering::Release);
        self.control.committed_sound_count.store(committed, Ordering::Release);
        Ok(id)
    }

    pub fn sound_mapping(&self, id: AeSoundId) -> Result<AeSoundMapping, AeResult> {
        self.check_fatal_err()?;
        self.control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?
            .mapping(id)
    }

    pub fn sound_commit(&self, id: AeSoundId) -> Result<(), AeResult> {
        self.check_fatal_err()?;
        self.control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?
            .commit(id)
    }

    pub fn sound_free(&self, id: AeSoundId) -> Result<(), AeResult> {
        self.collect_garbage()?;
        self.control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?
            .free(id)
    }

    pub fn voice_play(&self, desc: &AePlayVoiceDesc) -> Result<AeVoiceId, AeResult> {
        self.check_fatal_err()?;
        self.collect_garbage()?;

        if desc.start_frame.0.is_some() // TODO: support for non-`None` start_frame 
            || !desc.gain.is_finite()
            || desc.gain < 0.0
            || !desc.playback_rate.is_finite()
            || desc.playback_rate != 1.0 // TODO: support for non-`1.0` playback_rate
            || desc.flags & !AE_VOICE_FLAG_LOOP != 0
            || desc.reserved0 != 0
        {
            return Err(AeResult::BadVoiceDesc);
        }

        let sound = self
            .control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?
            .resolve_committed(desc.sound)?;

        if desc.source_frame >= sound.desc.frame_count {
            return Err(AeResult::BadVoiceDesc);
        }

        let output_sample_rate = self.control.sample_rate.load(Ordering::Acquire);
        if output_sample_rate == 0 || sound.desc.sample_rate != output_sample_rate {
            return Err(AeResult::UnsupportedPlayback);
        }

        let mut state = self
            .control
            .producer
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?;

        if state.available_voice_credits == 0 {
            let block = VoiceBlock::try_new().map_err(|()| AeResult::OutOfMemory)?;
            let ptr = NonNull::from(Box::leak(block));

            if state
                .producer
                .push(RtCommand::AddVoiceBlock { block: ptr })
                .is_err()
            {
                // SAFETY: queue insertion failed means ownership never left this thread
                unsafe { drop(Box::from_raw(ptr.as_ptr())) };
                return Err(AeResult::CommandQueueFull);
            }

            state.available_voice_credits = VOICES_PER_BLOCK;
        }

        let id = state.next_voice_id.ok_or(AeResult::IdExhausted)?;
        state.next_voice_id = id.0.checked_add(1).map(AeVoiceId);

        let command = RtCommand::StartVoice {
            id,
            sound,
            source_frame: desc.source_frame,
            gain: desc.gain,
            playback_rate: desc.playback_rate,
            flags: desc.flags,
        };

        if state.producer.push(command).is_err() {
            return Err(AeResult::CommandQueueFull);
        }

        state.available_voice_credits -= 1;
        Ok(id)
    }

    pub fn voice_stop(&self, id: AeVoiceId) -> Result<(), AeResult> {
        self.check_fatal_err()?;
        if id.0 == 0 {
            return Err(AeResult::VoiceNotFound);
        }

        self.collect_garbage()?;
        let mut state = self
            .control
            .producer
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?;
        state
            .producer
            .push(RtCommand::StopVoice { id })
            .map_err(|_| AeResult::CommandQueueFull)
    }

    pub fn fill_stats(&self, out: &mut AeStats, cpu_load: f32) {
        out.mix_frame = self.control.acquire_mix_frame();
        out.sample_rate = self.control.sample_rate.load(Ordering::Acquire);
        out.quantum_frames = self.control.quantum_frames.load(Ordering::Acquire);
        out.channel_count = self.control.channels;
        out.xrun_count = self.control.xrun_count.load(Ordering::Acquire);
        out.running = i32::from(self.control.running.load(Ordering::Acquire));
        out.cpu_load = cpu_load;
        out.active_voice_count = self.control.active_voice_count.load(Ordering::Acquire);
        out.voice_block_count = self.control.voice_block_count.load(Ordering::Acquire);
        out.failed_voice_start_count = self
            .control
            .failed_voice_start_count
            .load(Ordering::Acquire);
        out.allocated_sound_count = self
            .control
            .allocated_sound_count
            .load(Ordering::Acquire);
        out.committed_sound_count = self
            .control
            .committed_sound_count
            .load(Ordering::Acquire);
        out.rt_error = self.control.acquire_rt_error();
    }

    fn check_fatal_err(&self) -> Result<(), AeResult> {
        match self.control.acquire_rt_error() {
            AeResult::Ok => Ok(()),
            e => Err(e),
        }
    }
}

impl RtState {
    pub fn process_stereo(&mut self, control: &ControlState, left: &mut [f32], right: &mut [f32]) {
        left.fill(0.0);
        right.fill(0.0);

        if control.has_rt_error() {
            return;
        }

        while let Ok(command) = self.command_consumer.pop() {
            self.apply_command(control, command);
        }

        self.voices
            .render_stereo(left, right, &mut self.maintenance_producer);
        self.voices
            .reclaim_empty_blocks(&mut self.maintenance_producer);

        let nframes = left.len().min(right.len()) as u64;
        let Some(next) = self.mix_frame.checked_add_frames(nframes) else {
            control.publish_rt_error(AeResult::FrameExhausted);
            return;
        };
        self.mix_frame = next;
        control.publish_mix_frame(next);

        control
            .active_voice_count
            .store(self.voices.active_count(), Ordering::Release);
        control
            .voice_block_count
            .store(self.voices.block_count(), Ordering::Release);
    }

    fn apply_command(&mut self, control: &ControlState, command: RtCommand) {
        match command {
            RtCommand::AddVoiceBlock { block } => {
                // SAFETY: control thread transferred exclusive ownership by successfully enqueueing
                // this pointer exactly once
                let block = unsafe { Box::from_raw(block.as_ptr()) };
                self.voices.add_block(block);
            }
            RtCommand::StartVoice {
                id,
                sound,
                source_frame,
                gain,
                playback_rate,
                flags,
            } => {
                let source_step = f64::from(playback_rate);
                let voice = ActiveVoice {
                    id,
                    sound,
                    source_position: source_frame as f64,
                    source_step,
                    gain,
                    flags,
                };

                if self.voices.start_voice(voice).is_err() {
                    control
                        .failed_voice_start_count
                        .fetch_add(1, Ordering::Release);
                    _ = self
                        .maintenance_producer
                        .push(MaintenanceEvent::VoiceSlotReleased);
                }
            }
            RtCommand::StopVoice { id } => {
                if self.voices.stop_voice(id) {
                    _ = self
                        .maintenance_producer
                        .push(MaintenanceEvent::VoiceSlotReleased);
                }
            }
        }
    }
}

impl Drop for RtState {
    fn drop(&mut self) {
        while let Ok(command) = self.command_consumer.pop() {
            if let RtCommand::AddVoiceBlock { block } = command {
                // SAFETY: the queued ownership transfer was never consumed by the RT thread
                unsafe { drop(Box::from_raw(block.as_ptr())) };
            }
        }
    }
}
