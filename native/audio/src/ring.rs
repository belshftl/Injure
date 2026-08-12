// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::num::NonZero;
use std::ptr::NonNull;
use std::sync::Arc;

use crate::assets::SoundAsset;
use crate::engine::MixFrame;
use crate::voices::{AeVoiceId, VoiceBlock};

#[derive(Debug)]
pub struct OwnedRingAllocation<T: Send>(Box<T>);

impl<T: Send> OwnedRingAllocation<T> {
    pub fn new(value: T) -> Self {
        Self(Box::new(value))
    }

    pub fn from_box(value: Box<T>) -> Self {
        Self(value)
    }

    pub fn into_ring(self) -> RingAllocation<T> {
        let Self(value) = self;
        RingAllocation(NonNull::from(Box::leak(value)))
    }
}

#[must_use = "dropping a ring allocation leaks it; reclaim it on a non-RT thread"]
#[derive(Debug)]
pub struct RingAllocation<T: Send>(NonNull<T>);

impl<T: Send> RingAllocation<T> {
    pub const fn as_ref(&self) -> &T {
        // SAFETY: this type owns a live `T` allocation
        unsafe { self.0.as_ref() }
    }

    pub const fn as_mut(&mut self) -> &mut T {
        // SAFETY: this type uniquely owns the `T` allocation, and `&mut self` prevents access
        // through this wrapper through the returned borrow's duration
        unsafe { self.0.as_mut() }
    }

    pub fn into_link(self) -> RingLink<T> {
        RingLink(self.0)
    }

    pub unsafe fn from_link_unchecked(link: &RingLink<T>) -> Self {
        Self(link.get())
    }

    pub fn into_box(self) -> Box<T> {
        let Self(ptr) = self;
        // SAFETY: this type uniquely owns the `T` allocation
        unsafe { Box::from_raw(ptr.as_ptr()) }
    }

    pub fn reclaim(self) {
        drop(self.into_box());
    }
}

// SAFETY: transferring the `RingAllocation<T>` wrapper transfers exclusive ownership of `T`
unsafe impl<T: Send> Send for RingAllocation<T> {}

#[derive(Debug)]
pub struct RingLink<T: Send>(NonNull<T>);

impl<T: Send> RingLink<T> {
    pub const fn get(&self) -> NonNull<T> {
        self.0
    }

    pub const unsafe fn as_ref(&self) -> &T {
        unsafe { self.0.as_ref() }
    }

    pub const unsafe fn as_mut(&mut self) -> &mut T {
        unsafe { self.0.as_mut() }
    }
}

// SAFETY: RingLink<T> is only a pointer value, dereferencing requires explicit unsafe code and
// exclusive-ownership correctness is left to the caller
unsafe impl<T: Send> Send for RingLink<T> {}

#[derive(Debug)]
pub enum RtCommand {
    StartVoice {
        id: AeVoiceId,
        sound: Arc<SoundAsset>,
        source_frame: u64,
        gain: f32,
        playback_rate: f32, // TODO: switch this over to SourcePosition/SourceStep
        flags: u32,
    },
    StopVoice {
        id: AeVoiceId,
    },
}

#[derive(Debug)]
pub struct ScheduledCommand {
    pub target: Option<MixFrame>,
    pub sequence: u64,
    pub command: Option<RtCommand>,
    pub next: Option<RingLink<ScheduledCommand>>,
}

pub enum RtMessage {
    AddVoiceBlock(RingAllocation<VoiceBlock>),
    Schedule(RingAllocation<ScheduledCommand>),
}

pub enum MaintenanceEvent {
    ReclaimVoiceBlock {
        block: RingAllocation<VoiceBlock>,
        slots: NonZero<usize>, // TODO: drop this unless variable-sized blocks have a use
    },
    ReclaimScheduledCommand {
        command: RingAllocation<ScheduledCommand>,
    },
}
