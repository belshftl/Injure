// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::ptr::NonNull;
use std::sync::Arc;

use crate::assets::{SoundAsset};
use crate::voices::{AeVoiceId, VoiceBlock};

#[derive(Debug)]
pub enum RtCommand {
    AddVoiceBlock {
        block: NonNull<VoiceBlock>,
    },
    StartVoice {
        id: AeVoiceId,
        sound: Arc<SoundAsset>,
        source_frame: u64,
        gain: f32,
        playback_rate: f32,
        flags: u32,
    },
    StopVoice {
        id: AeVoiceId,
    },
}

// ownership of AddVoiceBlock pointers crosses the SPSC queue
unsafe impl Send for RtCommand {}

#[derive(Debug)]
pub enum MaintenanceEvent {
    VoiceSlotReleased,
    ReclaimVoiceBlock {
        block: NonNull<VoiceBlock>,
        slots: usize,
    },
}

unsafe impl Send for MaintenanceEvent {}
