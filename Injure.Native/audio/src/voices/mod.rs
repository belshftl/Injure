// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use rtrb::Producer;
use std::sync::Arc;

use crate::AeSoundId;
use crate::assets::SoundAsset;
use crate::engine::{AeOptionalMixFrame, SourcePosition, SourceStep};
use crate::ring::{MaintenanceEvent, OwnedRingAllocation, RingAllocation};

// pub const AE_VOICE_FLAG_NONE: u32 = 0;
pub const AE_VOICE_FLAG_LOOP: u32 = 1 << 0;

pub const VOICES_PER_BLOCK: usize = 64;

#[repr(transparent)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct AeVoiceId(pub u64);

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AePlayVoiceDesc {
    pub sound: AeSoundId,
    pub start_frame: AeOptionalMixFrame,
    pub source_frame: u64,
    pub gain: f32,
    pub playback_rate: f32,
    pub flags: u32,
    pub reserved0: u32,
}

#[derive(Debug)]
pub struct ActiveVoice {
    pub id: AeVoiceId,
    pub sound: Arc<SoundAsset>,
    pub source_position: SourcePosition,
    pub source_step: SourceStep,
    pub gain: f32,
    pub flags: u32,
}

#[derive(Debug)]
pub struct VoiceSlot {
    active: Option<ActiveVoice>,
}

#[derive(Debug)]
pub struct VoiceBlock {
    slots: [VoiceSlot; VOICES_PER_BLOCK],
    next: Option<Box<VoiceBlock>>,
}

impl VoiceBlock {
    pub fn try_new() -> Result<Box<Self>, std::collections::TryReserveError> {
        let mut slots = Vec::new();
        slots.try_reserve_exact(VOICES_PER_BLOCK)?;
        slots.resize_with(VOICES_PER_BLOCK, || VoiceSlot { active: None });
        let slots: Box<[VoiceSlot]> = slots.into_boxed_slice();
        let slots: Box<[VoiceSlot; VOICES_PER_BLOCK]> =
            slots.try_into().expect("voice block size should be fixed");
        Ok(Box::new(Self {
            slots: *slots,
            next: None,
        }))
    }

    fn active_count(&self) -> usize {
        self.slots
            .iter()
            .filter(|slot| slot.active.is_some())
            .count()
    }
}

pub struct VoicePool {
    head: Option<Box<VoiceBlock>>,
    active_count: usize,
    block_count: usize,
}

impl VoicePool {
    pub const fn new() -> Self {
        Self {
            head: None,
            active_count: 0,
            block_count: 0,
        }
    }

    pub fn add_block(&mut self, block: RingAllocation<VoiceBlock>) {
        let mut block = block.into_box();
        block.next = self.head.take();
        self.head = Some(block);
        self.block_count += 1;
    }

    pub fn start(&mut self, voice: ActiveVoice) -> Result<(), ActiveVoice> {
        let mut block = self.head.as_deref_mut();
        while let Some(current) = block {
            if let Some(slot) = current.slots.iter_mut().find(|slot| slot.active.is_none()) {
                slot.active = Some(voice);
                self.active_count += 1;
                return Ok(());
            }
            block = current.next.as_deref_mut();
        }
        Err(voice)
    }

    pub fn stop(&mut self, id: AeVoiceId) -> bool {
        let mut block = self.head.as_deref_mut();
        while let Some(current) = block {
            for slot in &mut current.slots {
                if slot.active.as_ref().is_some_and(|voice| voice.id == id) {
                    slot.active = None;
                    self.active_count -= 1;
                    return true;
                }
            }
            block = current.next.as_deref_mut();
        }
        false
    }

    pub fn render_stereo(&mut self, left: &mut [f32], right: &mut [f32]) -> usize {
        let mut released = 0;
        let mut block = self.head.as_deref_mut();
        while let Some(current) = block {
            for slot in &mut current.slots {
                let Some(voice) = slot.active.as_mut() else {
                    continue;
                };

                if !render_voice(voice, left, right) {
                    slot.active = None;
                    self.active_count -= 1;
                    released += 1;
                }
            }
            block = current.next.as_deref_mut();
        }
        released
    }

    pub fn reclaim_empty_blocks(&mut self, maintenance: &mut Producer<MaintenanceEvent>) {
        let mut empty_seen = 0usize;
        let mut cursor = &mut self.head;
        while cursor.is_some() {
            let is_empty = cursor
                .as_ref()
                .is_some_and(|block| block.active_count() == 0);

            if is_empty {
                empty_seen += 1;
            }

            if is_empty && empty_seen > 1 {
                let mut removed = cursor.take().expect("cursor should contain a block");
                *cursor = removed.next.take();
                self.block_count -= 1;
                let allocation = OwnedRingAllocation::from_box(removed).into_ring();
                let event = MaintenanceEvent::ReclaimVoiceBlock {
                    block: allocation,
                    slots: nz::usize!(VOICES_PER_BLOCK),
                };
                if let Err(event) = maintenance.push(event) {
                    let rtrb::PushError::Full(MaintenanceEvent::ReclaimVoiceBlock {
                        block, ..
                    }) = event
                    else {
                        unreachable!()
                    };
                    let mut restored = block.into_box();
                    restored.next = cursor.take();
                    *cursor = Some(restored);
                    self.block_count += 1;
                    return;
                }
                continue;
            }

            cursor = &mut cursor.as_mut().expect("cursor should contain a block").next;
        }
    }

    pub fn active_count(&self) -> usize {
        self.active_count
    }

    pub fn block_count(&self) -> usize {
        self.block_count
    }
}

fn render_voice(voice: &mut ActiveVoice, left: &mut [f32], right: &mut [f32]) -> bool {
    let channels = voice.sound.desc.channels;
    let frame_count = voice.sound.desc.frame_count;
    let nframes = left.len().min(right.len());

    for i in 0..nframes {
        let Ok(mut source_frame) = usize::try_from(voice.source_position.whole) else {
            return false;
        };

        if source_frame >= frame_count {
            if voice.flags & AE_VOICE_FLAG_LOOP == 0 {
                return false;
            }
            voice.source_position.whole %= frame_count as u64;
            source_frame %= frame_count;
        }

        let base = source_frame * channels;
        match channels {
            1 => {
                let sample = voice.sound.pcm[base] * voice.gain;
                left[i] += sample;
                right[i] += sample;
            }
            2 => {
                left[i] += voice.sound.pcm[base] * voice.gain;
                right[i] += voice.sound.pcm[base + 1] * voice.gain;
            }
            _ => return false,
        }

        if !voice.source_position.advance(voice.source_step) {
            return false;
        }
    }
    true
}
