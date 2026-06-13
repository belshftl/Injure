// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use rtrb::{Consumer, Producer, RingBuffer};
use std::ptr::NonNull;
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, AtomicI32, AtomicI64, AtomicU64, Ordering};

use crate::assets::{AeSoundDesc, AeSoundId, AeSoundMapping, SoundTable};
use crate::commands::{
    AE_FRAME_IMMEDIATE, AE_VOICE_FLAG_LOOP, AePlayVoiceDesc, AeResult, AeVoiceId, MaintenanceEvent,
    RtCommand,
};
use crate::voices::{ActiveVoice, VOICES_PER_BLOCK, VoiceBlock, VoiceBlockId, VoicePool};

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AeConfig {
    pub channels: i32,
    pub command_capacity: i32,
    pub maintenance_capacity: i32,
}

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AeStats {
    pub frame_cursor: i64,
    pub sample_rate: i32,
    pub quantum_frames: i32,
    pub channel_count: i32,
    pub xrun_count: i64,
    pub running: i32,
    pub cpu_load: f32,
    pub active_voice_count: u64,
    pub voice_block_count: u64,
    pub failed_voice_start_count: u64,
    pub allocated_sound_count: u64,
    pub committed_sound_count: u64,
}

struct ProducerState {
    producer: Producer<RtCommand>,
    next_voice_id: Option<AeVoiceId>,
    next_block_id: Option<VoiceBlockId>,
    available_voice_credits: usize,
}

pub struct ControlState {
    pub channels: i32,
    pub sample_rate: AtomicI32,
    pub quantum_frames: AtomicI32,
    pub frame_cursor: AtomicI64,
    pub xrun_count: AtomicI64,
    pub running: AtomicBool,
    pub active_voice_count: AtomicU64,
    pub voice_block_count: AtomicU64,
    pub failed_voice_start_count: AtomicU64,
    producer: Mutex<ProducerState>,
    maintenance_consumer: Mutex<Consumer<MaintenanceEvent>>,
    sounds: Mutex<SoundTable>,
}

pub struct RtState {
    frame_cursor: i64,
    command_consumer: Consumer<RtCommand>,
    maintenance_producer: Producer<MaintenanceEvent>,
    voices: VoicePool,
}

pub struct Engine {
    pub control: Box<ControlState>,
    pub rt: Box<RtState>,
}

impl Engine {
    pub fn new(config: &AeConfig) -> Self {
        let command_capacity = config.command_capacity.max(16) as usize;
        let maintenance_capacity = config.maintenance_capacity.max(16) as usize;
        let (command_producer, command_consumer) = RingBuffer::new(command_capacity);
        let (maintenance_producer, maintenance_consumer) = RingBuffer::new(maintenance_capacity);
        let channels = if config.channels <= 0 {
            2
        } else {
            config.channels
        };

        Self {
            control: Box::new(ControlState {
                channels,
                sample_rate: AtomicI32::new(0),
                quantum_frames: AtomicI32::new(0),
                frame_cursor: AtomicI64::new(0),
                xrun_count: AtomicI64::new(0),
                running: AtomicBool::new(false),
                active_voice_count: AtomicU64::new(0),
                voice_block_count: AtomicU64::new(0),
                failed_voice_start_count: AtomicU64::new(0),
                producer: Mutex::new(ProducerState {
                    producer: command_producer,
                    next_voice_id: Some(AeVoiceId(1)),
                    next_block_id: Some(VoiceBlockId(1)),
                    available_voice_credits: 0,
                }),
                maintenance_consumer: Mutex::new(maintenance_consumer),
                sounds: Mutex::new(SoundTable::new()),
            }),
            rt: Box::new(RtState {
                frame_cursor: 0,
                command_consumer,
                maintenance_producer,
                voices: VoicePool::new(),
            }),
        }
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
                    id: _,
                    slots,
                } => {
                    producer.available_voice_credits = producer
                        .available_voice_credits
                        .saturating_sub(slots as usize);
                    // SAFETY: RT thread removed this block from its linked list and transferred
                    // exclusive ownership through the maintenance ring
                    unsafe { drop(Box::from_raw(block.as_ptr())) };
                }
            }
        }

        Ok(())
    }

    pub fn sound_alloc(&self, desc: &AeSoundDesc) -> Result<AeSoundId, AeResult> {
        self.collect_garbage()?;
        self.control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?
            .allocate(desc)
    }

    pub fn sound_mapping(&self, id: AeSoundId) -> Result<AeSoundMapping, AeResult> {
        self.control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?
            .mapping(id)
    }

    pub fn sound_commit(&self, id: AeSoundId) -> Result<(), AeResult> {
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
        self.collect_garbage()?;

        if desc.start_frame != AE_FRAME_IMMEDIATE
            || !desc.gain.is_finite()
            || desc.gain < 0.0
            || !desc.playback_rate.is_finite()
            || desc.playback_rate != 1.0
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
        if output_sample_rate <= 0 || sound.desc.sample_rate != output_sample_rate as u32 {
            return Err(AeResult::UnsupportedPlayback);
        }

        let mut state = self
            .control
            .producer
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?;

        if state.available_voice_credits == 0 {
            let block_id = state.next_block_id.ok_or(AeResult::IdExhausted)?;
            let block = VoiceBlock::try_new(block_id).map_err(|()| AeResult::OutOfMemory)?;
            state.next_block_id = block_id.0.checked_add(1).map(VoiceBlockId);
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

    pub fn fill_stats(&self, out: &mut AeStats, cpu_load: f32) -> Result<(), AeResult> {
        self.collect_garbage()?;
        out.frame_cursor = self.control.frame_cursor.load(Ordering::Acquire);
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

        let sounds = self
            .control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?;
        (out.allocated_sound_count, out.committed_sound_count) = sounds.counts();
        Ok(())
    }
}

impl RtState {
    pub fn process_stereo(&mut self, control: &ControlState, left: &mut [f32], right: &mut [f32]) {
        left.fill(0.0);
        right.fill(0.0);

        while let Ok(command) = self.command_consumer.pop() {
            self.apply_command(control, command);
        }

        self.voices
            .render_stereo(left, right, &mut self.maintenance_producer);
        self.voices
            .reclaim_empty_blocks(&mut self.maintenance_producer);

        self.frame_cursor += left.len().min(right.len()) as i64;
        control
            .frame_cursor
            .store(self.frame_cursor, Ordering::Release);
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
                    let _ = self
                        .maintenance_producer
                        .push(MaintenanceEvent::VoiceSlotReleased);
                }
            }
            RtCommand::StopVoice { id } => {
                if self.voices.stop_voice(id) {
                    let _ = self
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
