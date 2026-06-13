// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use rtrb::Producer;
use std::ptr::NonNull;
use std::sync::Arc;

use crate::assets::SoundAsset;
use crate::commands::{AE_VOICE_FLAG_LOOP, AeVoiceId, MaintenanceEvent};

pub const VOICES_PER_BLOCK: usize = 64;

#[repr(transparent)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct VoiceBlockId(pub u64);

pub struct ActiveVoice {
    pub id: AeVoiceId,
    pub sound: Arc<SoundAsset>,
    pub source_position: f64,
    pub source_step: f64,
    pub gain: f32,
    pub flags: u32,
}

pub struct VoiceSlot {
    pub active: Option<ActiveVoice>,
}

pub struct VoiceBlock {
    pub id: VoiceBlockId,
    pub slots: Box<[VoiceSlot]>,
    pub next: Option<Box<VoiceBlock>>,
}

impl VoiceBlock {
    pub fn try_new(id: VoiceBlockId) -> Result<Box<Self>, ()> {
        let mut slots = Vec::new();
        slots.try_reserve_exact(VOICES_PER_BLOCK).map_err(|_| ())?;
        slots.resize_with(VOICES_PER_BLOCK, || VoiceSlot { active: None });
        Ok(Box::new(Self {
            id,
            slots: slots.into_boxed_slice(),
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
    active_count: u64,
    block_count: u64,
}

impl VoicePool {
    pub fn new() -> Self {
        Self {
            head: None,
            active_count: 0,
            block_count: 0,
        }
    }

    pub fn add_block(&mut self, mut block: Box<VoiceBlock>) {
        block.next = self.head.take();
        self.head = Some(block);
        self.block_count += 1;
    }

    pub fn start_voice(&mut self, voice: ActiveVoice) -> Result<(), ActiveVoice> {
        let mut block = self.head.as_deref_mut();
        while let Some(current) = block {
            for slot in &mut current.slots {
                if slot.active.is_none() {
                    slot.active = Some(voice);
                    self.active_count += 1;
                    return Ok(());
                }
            }
            block = current.next.as_deref_mut();
        }
        Err(voice)
    }

    pub fn stop_voice(&mut self, id: AeVoiceId) -> bool {
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

    pub fn render_stereo(
        &mut self,
        left: &mut [f32],
        right: &mut [f32],
        maintenance: &mut Producer<MaintenanceEvent>,
    ) {
        let mut block = self.head.as_deref_mut();
        while let Some(current) = block {
            for slot in &mut current.slots {
                let Some(voice) = slot.active.as_mut() else {
                    continue;
                };

                if !render_voice(voice, left, right) {
                    slot.active = None;
                    self.active_count -= 1;
                    _ = maintenance.push(MaintenanceEvent::VoiceSlotReleased);
                }
            }
            block = current.next.as_deref_mut();
        }
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

                let id = removed.id;
                let ptr = NonNull::from(Box::leak(removed));
                let event = MaintenanceEvent::ReclaimVoiceBlock {
                    block: ptr,
                    id,
                    slots: VOICES_PER_BLOCK as u32,
                };

                if maintenance.push(event).is_err() {
                    // SAFETY: push failed means ownership didn't cross the queue
                    let mut restored = unsafe { Box::from_raw(ptr.as_ptr()) };
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

    pub fn active_count(&self) -> u64 {
        self.active_count
    }

    pub fn block_count(&self) -> u64 {
        self.block_count
    }
}

fn render_voice(voice: &mut ActiveVoice, left: &mut [f32], right: &mut [f32]) -> bool {
    let channels = voice.sound.desc.channels as usize;
    let frame_count = voice.sound.desc.frame_count as usize;
    let nframes = left.len().min(right.len());

    for i in 0..nframes {
        let mut source_frame = voice.source_position as usize;
        if source_frame >= frame_count {
            if voice.flags & AE_VOICE_FLAG_LOOP == 0 {
                return false;
            }
            voice.source_position %= frame_count as f64;
            source_frame = voice.source_position as usize;
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

        voice.source_position += voice.source_step;
    }

    true
}
