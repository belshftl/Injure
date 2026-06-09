// SPDX-License-Identifier: MIT

pub const AE_COMMAND_NONE: u32 = 0;
pub const AE_COMMAND_SET_TEST_TONE: u32 = 1;

pub const AE_COMMAND_IMMEDIATE: i64 = i64::MIN;

#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AeResult {
    Ok = 0,
    Null = -1,
    BadState = -2,
    JackOpenFailed = -3,
    JackPortFailed = -4,
    JackActivateFailed = -5,
    NoOutputPorts = -6,
    CommandQueueFull = -7,
    LockPoisoned = -8,
    BadCommand = -9,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct AeSetTestToneCommand {
    pub enabled: i32,
    pub hz: f32,
    pub gain: f32,
    pub _reserved0: i32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub union AeCommandData {
    pub set_test_tone: AeSetTestToneCommand,
    pub raw: [u64; 8],
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct AeCommand {
    pub kind: u32,
    pub size: u32,
    pub target_frame: i64,
    pub data: AeCommandData,
}

impl AeCommand {
    pub fn is_abi_valid(&self) -> bool {
        self.size == core::mem::size_of::<AeCommand>() as u32
            && self.kind == AE_COMMAND_SET_TEST_TONE
    }

    pub fn is_semantically_valid(&self) -> bool {
        if !self.is_abi_valid() {
            return false;
        }

        // scheduling is not implemented yet
        if self.target_frame != AE_COMMAND_IMMEDIATE {
            return false;
        }

        #[allow(clippy::single_match)]
        match self.kind {
            AE_COMMAND_SET_TEST_TONE => {
                // SAFETY: this union field selected by `self.kind`
                let c = unsafe { self.data.set_test_tone };
                (c.enabled == 0 || c.enabled == 1)
                    && c.hz.is_finite()
                    && c.hz >= 0.0
                    && c.hz <= 200000.0
                    && c.gain.is_finite()
                    && c.gain >= 0.0
                    && c.gain <= 1.0
            }
            _ => false,
        }
    }
}
