// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use rtrb::{Consumer, Producer, RingBuffer};
use std::num::NonZero;
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, AtomicUsize, Ordering};

use crate::AeResult;
use crate::assets::{AeSoundDesc, AeSoundId, AeSoundMapping, SoundTable};
use crate::ring::{
    MaintenanceEvent, OwnedRingAllocation, RingAllocation, RingLink, RtCommand, RtMessage,
    ScheduledCommand,
};
use crate::voices::{
    AE_VOICE_FLAG_LOOP, ActiveVoice, AePlayVoiceDesc, AeVoiceId, VOICES_PER_BLOCK, VoiceBlock,
    VoicePool,
};

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
        NonZero::new(value).map(Self)
    }

    pub fn get(self) -> u64 {
        self.0.get()
    }

    pub fn checked_add_frames(self, frames: u64) -> Option<Self> {
        let value = self.get().checked_add(frames)?;
        NonZero::new(value).map(Self)
    }

    pub fn distance_from(self, earlier: Self) -> Option<usize> {
        usize::try_from(self.get().checked_sub(earlier.get())?).ok()
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SourcePosition {
    pub whole: u64,
    pub frac: u64,
}

impl SourcePosition {
    pub const fn from_frame(frame: u64) -> Self {
        Self {
            whole: frame,
            frac: 0,
        }
    }

    pub fn advance(&mut self, step: SourceStep) -> bool {
        let (frac, carry) = self.frac.overflowing_add(step.frac);
        let Some(whole) = self
            .whole
            .checked_add(step.whole)
            .and_then(|value| value.checked_add(u64::from(carry)))
        else {
            return false;
        };

        self.whole = whole;
        self.frac = frac;
        true
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SourceStep {
    pub whole: u64,
    pub frac: u64,
}

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AeConfig {
    pub channels: u32,
    pub message_queue_capacity: usize,
    pub maintenance_capacity: usize,
}

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AeStats {
    pub mix_frame: MixFrame,
    pub sample_rate: u32,
    pub quantum_frames: u32,
    pub channel_count: u32,
    pub running: u32,
    pub xrun_count: u64,
    pub active_voice_count: u64,
    pub voice_block_count: u64,
    pub failed_voice_start_count: u64,
    pub allocated_sound_count: u64,
    pub committed_sound_count: u64,
    pub cpu_load: f32,
    pub rt_error: AeResult,
}

struct ProducerState {
    producer: Producer<RtMessage>,
    next_voice_id: Option<AeVoiceId>,
    available_voice_credits: usize,
}

pub struct ControlState {
    pub channels: u32,
    pub sample_rate: AtomicU32,
    pub quantum_frames: AtomicU32,
    pub running: AtomicBool,
    pub xrun_count: AtomicU64,
    pub active_voice_count: AtomicU64,
    pub voice_block_count: AtomicU64,
    pub failed_voice_start_count: AtomicU64,
    mix_frame: AtomicU64,
    producer: Mutex<ProducerState>,
    maintenance_consumer: Mutex<Consumer<MaintenanceEvent>>,
    sounds: Mutex<SoundTable>,
    allocated_sound_count: AtomicU64,
    committed_sound_count: AtomicU64,
    returned_voice_credits: AtomicUsize,
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
        AeResult::try_from(raw)
            .expect("ControlState.rt_error must contain a valid AeResult numeric value")
    }

    pub fn publish_rt_error(&self, error: AeResult) {
        debug_assert_ne!(error, AeResult::Ok);
        if error == AeResult::Ok {
            return;
        }
        _ = self.rt_error.compare_exchange(
            AeResult::Ok as u32,
            error as u32,
            Ordering::Release,
            Ordering::Relaxed,
        );
    }

    #[inline]
    pub fn has_rt_error(&self) -> bool {
        self.rt_error.load(Ordering::Acquire) != AeResult::Ok as u32
    }
}

pub struct RtState {
    mix_frame: MixFrame,
    message_consumer: Consumer<RtMessage>,
    maintenance_producer: Producer<MaintenanceEvent>,
    voices: VoicePool,
    scheduled_head: Option<RingLink<ScheduledCommand>>,
    reclaim_head: Option<RingLink<ScheduledCommand>>,
    next_sequence: Option<u64>,
}

pub struct Engine {
    pub control: Box<ControlState>,
    pub rt: Box<RtState>,
}

impl Engine {
    pub fn try_new(config: &AeConfig) -> Result<Self, AeResult> {
        if config.channels == 0
            || config.message_queue_capacity == 0
            || config.maintenance_capacity == 0
        {
            return Err(AeResult::Null);
        }

        let (message_producer, message_consumer) = RingBuffer::new(config.message_queue_capacity);
        let (maintenance_producer, maintenance_consumer) =
            RingBuffer::new(config.maintenance_capacity);

        Ok(Self {
            control: Box::new(ControlState {
                channels: config.channels,
                sample_rate: AtomicU32::new(0),
                quantum_frames: AtomicU32::new(0),
                running: AtomicBool::new(false),
                xrun_count: AtomicU64::new(0),
                active_voice_count: AtomicU64::new(0),
                voice_block_count: AtomicU64::new(0),
                failed_voice_start_count: AtomicU64::new(0),
                mix_frame: AtomicU64::new(1), // again, 0 is not a valid value
                producer: Mutex::new(ProducerState {
                    producer: message_producer,
                    next_voice_id: Some(AeVoiceId(1)),
                    available_voice_credits: 0,
                }),
                maintenance_consumer: Mutex::new(maintenance_consumer),
                sounds: Mutex::new(SoundTable::new()),
                allocated_sound_count: AtomicU64::new(0),
                committed_sound_count: AtomicU64::new(0),
                returned_voice_credits: AtomicUsize::new(0),
                rt_error: AtomicU32::new(AeResult::Ok as u32),
            }),
            rt: Box::new(RtState {
                mix_frame: MixFrame::FIRST,
                message_consumer,
                maintenance_producer,
                voices: VoicePool::new(),
                scheduled_head: None,
                reclaim_head: None,
                next_sequence: Some(0),
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

        let returned = self
            .control
            .returned_voice_credits
            .swap(0, Ordering::Relaxed);
        producer.available_voice_credits =
            producer.available_voice_credits.saturating_add(returned);

        while let Ok(event) = maintenance.pop() {
            match event {
                MaintenanceEvent::ReclaimVoiceBlock { block, slots } => {
                    producer.available_voice_credits =
                        producer.available_voice_credits.saturating_sub(slots.get());
                    // SAFETY: RT thread removed this block from its linked list and transferred
                    // exclusive ownership through the maintenance ring
                    block.reclaim();
                }
                MaintenanceEvent::ReclaimScheduledCommand { command } => {
                    command.reclaim();
                }
            }
        }

        Ok(())
    }

    pub fn sound_alloc(&self, desc: &AeSoundDesc) -> Result<AeSoundId, AeResult> {
        self.check_fatal_err()?;
        self.collect_garbage()?;
        let mut sounds = self
            .control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?;
        let id = sounds.allocate(desc)?;
        let (allocated, committed) = sounds.counts();
        self.control
            .allocated_sound_count
            .store(allocated, Ordering::Release);
        self.control
            .committed_sound_count
            .store(committed, Ordering::Release);
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

        if !desc.gain.is_finite()
            || desc.gain < 0.0
            || !desc.playback_rate.is_finite()
            || desc.playback_rate != 1.0 // TODO: support for non-`1.0` playback_rate
            || desc.flags & !AE_VOICE_FLAG_LOOP != 0
            || desc.reserved0 != 0
        {
            return Err(AeResult::BadVoiceDesc);
        }

        let source_frame =
            usize::try_from(desc.source_frame).map_err(|_| AeResult::AllocationTooLarge)?;

        let sound = self
            .control
            .sounds
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?
            .resolve_committed(desc.sound)?;

        if source_frame >= sound.desc.frame_count {
            return Err(AeResult::BadVoiceDesc);
        }

        let output_sample_rate = self.control.sample_rate.load(Ordering::Acquire);
        if output_sample_rate == 0 || sound.desc.sample_rate != output_sample_rate as usize {
            return Err(AeResult::UnsupportedPlayback);
        }

        let mut state = self
            .control
            .producer
            .lock()
            .map_err(|_| AeResult::MutexPoisoned)?;

        if state.available_voice_credits == 0 {
            let block = VoiceBlock::try_new().map_err(|_| AeResult::OutOfMemory)?;
            let allocation = OwnedRingAllocation::from_box(block).into_ring();
            if let Err(message) = state.producer.push(RtMessage::AddVoiceBlock(allocation)) {
                let rtrb::PushError::Full(RtMessage::AddVoiceBlock(allocation)) = message else {
                    unreachable!()
                };
                allocation.reclaim();
                return Err(AeResult::MessageQueueFull);
            }
            state.available_voice_credits = VOICES_PER_BLOCK;
        }

        let id = state.next_voice_id.ok_or(AeResult::IdExhausted)?;
        state.next_voice_id = id.0.checked_add(1).map(AeVoiceId);

        let command = ScheduledCommand {
            target: desc.start_frame.to_mix_frame(),
            sequence: 0,
            command: Some(RtCommand::StartVoice {
                id,
                sound,
                source_frame: desc.source_frame,
                gain: desc.gain,
                playback_rate: desc.playback_rate,
                flags: desc.flags,
            }),
            next: None,
        };
        let allocation = OwnedRingAllocation::new(command).into_ring();
        if let Err(message) = state.producer.push(RtMessage::Schedule(allocation)) {
            let rtrb::PushError::Full(RtMessage::Schedule(allocation)) = message else {
                unreachable!()
            };
            allocation.reclaim();
            return Err(AeResult::MessageQueueFull);
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
        let command = ScheduledCommand {
            target: None,
            sequence: 0,
            command: Some(RtCommand::StopVoice { id }),
            next: None,
        };
        let allocation = OwnedRingAllocation::new(command).into_ring();
        if let Err(message) = state.producer.push(RtMessage::Schedule(allocation)) {
            let rtrb::PushError::Full(RtMessage::Schedule(allocation)) = message else {
                unreachable!()
            };
            allocation.reclaim();
            return Err(AeResult::MessageQueueFull);
        }
        Ok(())
    }

    pub fn fill_stats(&self, out: &mut AeStats, cpu_load: f32) {
        out.mix_frame = self.control.acquire_mix_frame();
        out.sample_rate = self.control.sample_rate.load(Ordering::Acquire);
        out.quantum_frames = self.control.quantum_frames.load(Ordering::Acquire);
        out.channel_count = self.control.channels;
        out.running = u32::from(self.control.running.load(Ordering::Acquire));
        out.xrun_count = self.control.xrun_count.load(Ordering::Acquire);
        out.active_voice_count = self.control.active_voice_count.load(Ordering::Acquire);
        out.voice_block_count = self.control.voice_block_count.load(Ordering::Acquire);
        out.failed_voice_start_count = self
            .control
            .failed_voice_start_count
            .load(Ordering::Acquire);
        out.allocated_sound_count = self.control.allocated_sound_count.load(Ordering::Acquire);
        out.committed_sound_count = self.control.committed_sound_count.load(Ordering::Acquire);
        out.cpu_load = cpu_load;
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

        let nframes = left.len().min(right.len());
        let block_start = self.mix_frame;
        let Some(block_end) = block_start.checked_add_frames(nframes as u64) else {
            control.publish_rt_error(AeResult::FrameExhausted);
            return;
        };

        self.retry_reclamation();
        self.receive_messages(control, block_start);
        if control.has_rt_error() {
            return;
        }

        let mut render_frame = block_start;
        let mut output_offset = 0usize;
        while let Some(target) = self.next_scheduled_target() {
            if target >= block_end {
                break;
            }
            let segment_frames = target
                .distance_from(render_frame)
                .expect("scheduler target must not precede render position");
            let segment_end = output_offset + segment_frames;
            let released = self.voices.render_stereo(
                &mut left[output_offset..segment_end],
                &mut right[output_offset..segment_end],
            );
            if released > 0 {
                control
                    .returned_voice_credits
                    .fetch_add(released, Ordering::Relaxed);
            }
            output_offset = segment_end;
            render_frame = target;
            self.execute_commands_at(control, target);
        }

        let released = self.voices.render_stereo(
            &mut left[output_offset..nframes],
            &mut right[output_offset..nframes],
        );
        if released > 0 {
            control
                .returned_voice_credits
                .fetch_add(released, Ordering::Relaxed);
        }
        self.voices
            .reclaim_empty_blocks(&mut self.maintenance_producer);
        self.mix_frame = block_end;
        control.publish_mix_frame(block_end);
        control
            .active_voice_count
            .store(self.voices.active_count() as u64, Ordering::Release);
        control
            .voice_block_count
            .store(self.voices.block_count() as u64, Ordering::Release);
    }

    fn receive_messages(&mut self, control: &ControlState, block_start: MixFrame) {
        while let Ok(message) = self.message_consumer.pop() {
            match message {
                RtMessage::AddVoiceBlock(block) => self.voices.add_block(block),
                RtMessage::Schedule(mut allocation) => {
                    let Some(sequence) = self.next_sequence else {
                        control.publish_rt_error(AeResult::IdExhausted);
                        self.defer_reclamation(allocation);
                        return;
                    };
                    self.next_sequence = sequence.checked_add(1);
                    let requested = allocation.as_ref().target;
                    let target = requested.map_or(block_start, |target| target.max(block_start));
                    allocation.as_mut().target = Some(target);
                    allocation.as_mut().sequence = sequence;
                    self.insert_scheduled(allocation);
                }
            }
        }
    }

    fn insert_scheduled(&mut self, allocation: RingAllocation<ScheduledCommand>) {
        let key = {
            let node = allocation.as_ref();
            (
                node.target
                    .expect("scheduler target should be resolved by now"),
                node.sequence,
            )
        };

        let mut cursor = &raw mut self.scheduled_head;
        while let Some(current_link) = unsafe { &mut *cursor } {
            let current = unsafe { current_link.as_mut() };
            let current_key = (
                current
                    .target
                    .expect("scheduler target should be resolved by now"),
                current.sequence,
            );
            if key < current_key {
                break;
            }
            cursor = &raw mut current.next;
        }

        let mut new_link = allocation.into_link();

        // SAFETY: `cursor` points to one `RingLink<T>` owned by this list, `new_link` is not yet
        // linked anywhere and is exclusively owned here
        unsafe {
            new_link.as_mut().next = (*cursor).take();
            *cursor = Some(new_link);
        }
    }

    fn next_scheduled_target(&self) -> Option<MixFrame> {
        self.scheduled_head.as_ref().map(|link| {
            unsafe { link.as_ref() }
                .target
                .expect("scheduler target should be resolved by now")
        })
    }

    fn pop_scheduled(&mut self) -> Option<RingAllocation<ScheduledCommand>> {
        let head = self.scheduled_head.take()?;
        // SAFETY: removing the head transfers ownership from the linked list into a RingAllocation
        let mut allocation = unsafe { RingAllocation::from_link_unchecked(&head) };
        self.scheduled_head = allocation.as_mut().next.take();
        Some(allocation)
    }

    fn execute_commands_at(&mut self, control: &ControlState, target: MixFrame) {
        while self.next_scheduled_target() == Some(target) {
            let mut allocation = self
                .pop_scheduled()
                .expect("scheduler linked list should have a head");
            let command = allocation
                .as_mut()
                .command
                .take()
                .expect("scheduled command should have a payload");
            self.apply_command(control, command);
            match self
                .maintenance_producer
                .push(MaintenanceEvent::ReclaimScheduledCommand {
                    command: allocation,
                }) {
                Ok(()) => {}
                Err(event) => {
                    let rtrb::PushError::Full(MaintenanceEvent::ReclaimScheduledCommand {
                        command,
                    }) = event
                    else {
                        unreachable!()
                    };
                    self.defer_reclamation(command);
                }
            }
        }
    }

    fn apply_command(&mut self, control: &ControlState, command: RtCommand) {
        match command {
            RtCommand::StartVoice {
                id,
                sound,
                source_frame,
                gain,
                playback_rate: _,
                flags,
            } => {
                let voice = ActiveVoice {
                    id,
                    sound,
                    source_position: SourcePosition::from_frame(source_frame),
                    source_step: SourceStep { whole: 1, frac: 0 }, // TODO: playback rate
                    gain,
                    flags,
                };
                if self.voices.start(voice).is_err() {
                    control
                        .failed_voice_start_count
                        .fetch_add(1, Ordering::Relaxed);
                    control
                        .returned_voice_credits
                        .fetch_add(1, Ordering::Relaxed);
                }
            }
            RtCommand::StopVoice { id } => {
                if self.voices.stop(id) {
                    control
                        .returned_voice_credits
                        .fetch_add(1, Ordering::Relaxed);
                }
            }
        }
    }

    fn defer_reclamation(&mut self, allocation: RingAllocation<ScheduledCommand>) {
        let mut link = allocation.into_link();
        unsafe { link.as_mut().next = self.reclaim_head.take() };
        self.reclaim_head = Some(link);
    }

    fn retry_reclamation(&mut self) {
        while let Some(link) = self.reclaim_head.take() {
            // SAFETY: removing the head transfers ownership from the linked list to here
            let mut allocation = unsafe { RingAllocation::from_link_unchecked(&link) };
            self.reclaim_head = allocation.as_mut().next.take();
            match self
                .maintenance_producer
                .push(MaintenanceEvent::ReclaimScheduledCommand {
                    command: allocation,
                }) {
                Ok(()) => {}
                Err(event) => {
                    let rtrb::PushError::Full(MaintenanceEvent::ReclaimScheduledCommand {
                        command,
                    }) = event
                    else {
                        unreachable!()
                    };
                    self.defer_reclamation(command);
                    break;
                }
            }
        }
    }
}

impl Drop for RtState {
    fn drop(&mut self) {
        while let Ok(message) = self.message_consumer.pop() {
            match message {
                RtMessage::AddVoiceBlock(block) => block.reclaim(),
                RtMessage::Schedule(command) => command.reclaim(),
            }
        }
        while let Some(head) = self.scheduled_head.take() {
            // SAFETY: removing the head transfers ownership from the linked list to here
            let mut allocation = unsafe { RingAllocation::from_link_unchecked(&head) };
            self.scheduled_head = allocation.as_mut().next.take();
            allocation.reclaim();
        }
        while let Some(head) = self.reclaim_head.take() {
            // SAFETY: removing the head transfers ownership from the linked list to here
            let mut allocation = unsafe { RingAllocation::from_link_unchecked(&head) };
            self.reclaim_head = allocation.as_mut().next.take();
            allocation.reclaim();
        }
    }
}
