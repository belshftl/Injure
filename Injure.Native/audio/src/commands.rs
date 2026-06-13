// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::ptr::NonNull;
use std::sync::Arc;

use crate::assets::{AeSoundId, SoundAsset};
use crate::voices::{VoiceBlock, VoiceBlockId};

pub const AE_VOICE_FLAG_NONE: u32 = 0;
pub const AE_VOICE_FLAG_LOOP: u32 = 1 << 0;
pub const AE_FRAME_IMMEDIATE: i64 = i64::MIN;

#[repr(transparent)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct AeVoiceId(pub u64);

#[repr(u32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[rustfmt::skip]
pub enum AeResult {
    Ok = 0,

    Null                = 0x80_000001,
    BadState            = 0x80_000002,
    CommandQueueFull    = 0x80_000003,
    MutexPoisoned       = 0x80_000004,
    BadSoundDesc        = 0x80_000005,
    SoundNotFound       = 0x80_000006,
    SoundBadState       = 0x80_000007,
    SoundInUse          = 0x80_000008,
    BadVoiceDesc        = 0x80_000009,
    VoiceNotFound       = 0x80_00000a,
    UnsupportedPlayback = 0x80_00000b,
    IdExhausted         = 0x80_00000c,

    JackOpenFailed      = 0x81_000001,
    JackPortFailed      = 0x81_000002,
    JackActivateFailed  = 0x81_000003,

    OutOfMemory         = 0xff_ffffff,
}

impl AeResult {
    pub fn code(self) -> u32 {
        self as u32
    }
}

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AePlayVoiceDesc {
    pub sound: AeSoundId,
    pub start_frame: i64,
    pub source_frame: u64,
    pub gain: f32,
    pub playback_rate: f32,
    pub flags: u32,
    pub reserved0: u32,
}

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
        id: VoiceBlockId,
        slots: u32,
    },
}

unsafe impl Send for MaintenanceEvent {}
