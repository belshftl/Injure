// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

#[allow(clippy::upper_case_acronyms)]
pub type HRESULT = i32;

pub const S_OK: HRESULT = 0;
pub const S_FALSE: HRESULT = 1;
pub const E_NOINTERFACE: HRESULT = -2_147_467_262; // 0x8000_4002
pub const E_POINTER: HRESULT = -2_147_467_261; // 0x8000_4003
pub const E_FAIL: HRESULT = -2_147_467_259; // 0x8000_4005
pub const E_INVALIDARG: HRESULT = -2_147_024_809; // 0x8007_0057
pub const CLASS_E_CLASSNOTAVAILABLE: HRESULT = -2_147_221_231; // 0x8004_0111

#[allow(clippy::upper_case_acronyms)]
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GUID {
    pub data1: u32,
    pub data2: u16,
    pub data3: u16,
    pub data4: [u8; 8],
}

impl GUID {
    pub const fn new(data1: u32, data2: u16, data3: u16, data4: [u8; 8]) -> Self {
        Self {
            data1,
            data2,
            data3,
            data4,
        }
    }
}

#[allow(non_upper_case_globals)]
pub const IID_IUnknown: GUID =
    GUID::new(0x0000_0000, 0x0000, 0x0000, [0xc0, 0, 0, 0, 0, 0, 0, 0x46]);

#[allow(non_upper_case_globals)]
pub const IID_IClassFactory: GUID =
    GUID::new(0x0000_0001, 0x0000, 0x0000, [0xc0, 0, 0, 0, 0, 0, 0, 0x46]);

#[allow(non_upper_case_globals)]
pub const IID_ICorProfilerCallback: [GUID; 4] = [
    GUID::new(
        0x176f_bed1,
        0xa55c,
        0x4796,
        [0x98, 0xca, 0xa9, 0xda, 0x0e, 0xf8, 0x83, 0xe7],
    ),
    GUID::new(
        0x8a8c_c829,
        0xccf2,
        0x49fe,
        [0xbb, 0xae, 0x0f, 0x02, 0x22, 0x28, 0x07, 0x1a],
    ),
    GUID::new(
        0x4fd2_ed52,
        0x7731,
        0x4b8d,
        [0x94, 0x69, 0x03, 0xd2, 0xcc, 0x30, 0x86, 0xc5],
    ),
    GUID::new(
        0x7b63_b2e3,
        0x107d,
        0x4d48,
        [0xb2, 0xf6, 0xf6, 0x1e, 0x22, 0x94, 0x70, 0xd2],
    ),
];

pub const CLSID_PROFILER: GUID = GUID::new(
    0xc240_1225,
    0xe6e4,
    0x434b,
    [0xb7, 0xfa, 0xe9, 0xc8, 0x49, 0xbb, 0x62, 0x71],
);

pub fn iid_eq(a: *const GUID, b: &GUID) -> bool {
    if a.is_null() {
        return false;
    }
    unsafe { *a == *b }
}

#[track_caller]
pub fn count(len: usize) -> u32 {
    u32::try_from(len).expect("length crossing into a COM signature should not have exceeded u32")
}
