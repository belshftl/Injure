// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::mem::size_of;
use std::sync::Arc;

use crate::AeResult;

pub const AE_SAMPLE_FORMAT_F32: u32 = 1;

#[repr(transparent)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct AeSoundId(pub u64);

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
struct SoundSlotIndex(u32);

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
struct SoundGeneration(u32);

#[repr(C)]
#[derive(Debug, Clone)]
pub struct AeSoundDesc {
    pub channels: u32,
    pub sample_rate: u32,
    pub frame_count: u64,
    pub format: u32,
    pub flags: u32,
}

#[repr(C)]
#[derive(Debug)]
pub struct AeSoundMapping {
    pub data: *mut core::ffi::c_void,
    pub byte_length: u64,
    pub frame_stride: u64,
}

pub struct SoundBuilder {
    pub desc: AeSoundDesc,
    pub pcm: Box<[f32]>,
}

pub struct SoundAsset {
    pub desc: AeSoundDesc,
    pub pcm: Box<[f32]>,
}

impl std::fmt::Debug for SoundAsset {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SoundAsset")
            .field("desc", &self.desc)
            .finish_non_exhaustive()
    }
}

pub enum SoundState {
    Allocated(SoundBuilder),
    Committed(Arc<SoundAsset>),
}

pub struct SoundSlot {
    generation: SoundGeneration,
    state: Option<SoundState>,
    retired: bool,
}

pub struct SoundTable {
    slots: Vec<SoundSlot>,
    free: Vec<SoundSlotIndex>,
}

impl SoundTable {
    pub fn new() -> Self {
        Self {
            slots: Vec::new(),
            free: Vec::new(),
        }
    }

    pub fn allocate(&mut self, desc: &AeSoundDesc) -> Result<AeSoundId, AeResult> {
        validate_desc(desc)?;

        let sample_count = desc
            .frame_count
            .checked_mul(u64::from(desc.channels))
            .and_then(|n| usize::try_from(n).ok())
            .ok_or(AeResult::BadSoundDesc)?;

        let mut pcm = Vec::<f32>::new();
        pcm.try_reserve_exact(sample_count)
            .map_err(|_| AeResult::OutOfMemory)?;
        pcm.resize(sample_count, 0.0);

        let state = SoundState::Allocated(SoundBuilder {
            desc: desc.clone(),
            pcm: pcm.into_boxed_slice(),
        });

        if let Some(index) = self.free.pop() {
            let slot = &mut self.slots[index.0 as usize];
            debug_assert!(!slot.retired && slot.state.is_none());
            slot.state = Some(state);
            return Ok(pack_id(index, slot.generation));
        }

        let index =
            SoundSlotIndex(u32::try_from(self.slots.len()).map_err(|_| AeResult::IdExhausted)?);
        self.slots
            .try_reserve(1)
            .map_err(|_| AeResult::OutOfMemory)?;
        let generation = SoundGeneration(1);
        self.slots.push(SoundSlot {
            generation,
            state: Some(state),
            retired: false,
        });
        Ok(pack_id(index, generation))
    }

    pub fn mapping(&mut self, id: AeSoundId) -> Result<AeSoundMapping, AeResult> {
        let slot = self.get_slot_mut(id)?;
        let Some(SoundState::Allocated(builder)) = slot.state.as_mut() else {
            return Err(AeResult::SoundBadState);
        };

        Ok(AeSoundMapping {
            data: builder.pcm.as_mut_ptr().cast(),
            byte_length: (builder.pcm.len() * size_of::<f32>()) as u64,
            frame_stride: u64::from(builder.desc.channels) * size_of::<f32>() as u64,
        })
    }

    pub fn commit(&mut self, id: AeSoundId) -> Result<(), AeResult> {
        let slot = self.get_slot_mut(id)?;
        let Some(state) = slot.state.take() else {
            return Err(AeResult::SoundNotFound);
        };

        let SoundState::Allocated(builder) = state else {
            slot.state = Some(state);
            return Err(AeResult::SoundBadState);
        };

        slot.state = Some(SoundState::Committed(Arc::new(SoundAsset {
            desc: builder.desc,
            pcm: builder.pcm,
        })));
        Ok(())
    }

    pub fn resolve_committed(&self, id: AeSoundId) -> Result<Arc<SoundAsset>, AeResult> {
        let slot = self.get_slot(id)?;
        let Some(SoundState::Committed(asset)) = slot.state.as_ref() else {
            return Err(AeResult::SoundBadState);
        };

        Ok(Arc::clone(asset))
    }

    pub fn free(&mut self, id: AeSoundId) -> Result<(), AeResult> {
        let (index, generation) = unpack_id(id)?;
        let slot = self
            .slots
            .get_mut(index.0 as usize)
            .ok_or(AeResult::SoundNotFound)?;
        if slot.retired || slot.generation != generation || slot.state.is_none() {
            return Err(AeResult::SoundNotFound);
        }

        if let Some(SoundState::Committed(asset)) = slot.state.as_ref()
            && Arc::strong_count(asset) != 1
        {
            return Err(AeResult::SoundInUse);
        }

        slot.state = None;
        match slot.generation.0.checked_add(1) {
            Some(next) => {
                slot.generation = SoundGeneration(next);
                self.free.push(index);
            }
            None => {
                slot.retired = true;
            }
        }
        Ok(())
    }

    pub fn counts(&self) -> (u64, u64) {
        let mut allocated = 0;
        let mut committed = 0;
        for slot in &self.slots {
            match slot.state.as_ref() {
                Some(SoundState::Allocated(_)) => allocated += 1,
                Some(SoundState::Committed(_)) => committed += 1,
                None => {}
            }
        }
        (allocated, committed)
    }

    fn get_slot(&self, id: AeSoundId) -> Result<&SoundSlot, AeResult> {
        let (index, generation) = unpack_id(id)?;
        let slot = self
            .slots
            .get(index.0 as usize)
            .ok_or(AeResult::SoundNotFound)?;
        if slot.retired || slot.generation != generation || slot.state.is_none() {
            Err(AeResult::SoundNotFound)
        } else {
            Ok(slot)
        }
    }

    fn get_slot_mut(&mut self, id: AeSoundId) -> Result<&mut SoundSlot, AeResult> {
        let (index, generation) = unpack_id(id)?;
        let slot = self
            .slots
            .get_mut(index.0 as usize)
            .ok_or(AeResult::SoundNotFound)?;
        if slot.retired || slot.generation != generation || slot.state.is_none() {
            Err(AeResult::SoundNotFound)
        } else {
            Ok(slot)
        }
    }
}

fn validate_desc(desc: &AeSoundDesc) -> Result<(), AeResult> {
    if desc.channels == 0
        || desc.channels > 2
        || desc.sample_rate == 0
        || desc.frame_count == 0
        || desc.format != AE_SAMPLE_FORMAT_F32
        || desc.flags != 0
    {
        Err(AeResult::BadSoundDesc)
    } else {
        Ok(())
    }
}

fn pack_id(index: SoundSlotIndex, generation: SoundGeneration) -> AeSoundId {
    AeSoundId((u64::from(generation.0) << 32) | u64::from(index.0))
}

fn unpack_id(id: AeSoundId) -> Result<(SoundSlotIndex, SoundGeneration), AeResult> {
    let index = SoundSlotIndex((id.0 & 0xffff_ffff) as u32);
    let generation = SoundGeneration((id.0 >> 32) as u32);
    if generation.0 == 0 {
        Err(AeResult::SoundNotFound)
    } else {
        Ok((index, generation))
    }
}
