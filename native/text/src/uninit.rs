// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::mem::MaybeUninit;

/// Writing into a `*mut MaybeUninit<T>`, i.e. what is used for a pointer to memory that is
/// writable but not initialized, such as out parameters of FFI exports.
pub trait UninitPtr<T> {
    /// Writes `value` without reading or dropping the old contents.
    ///
    /// # Safety
    ///
    /// The pointer must be valid for writes and properly aligned, as for [`std::ptr::write`].
    unsafe fn write_value(self, value: T);
}

impl<T> UninitPtr<T> for *mut MaybeUninit<T> {
    unsafe fn write_value(self, value: T) {
        // SAFETY: `MaybeUninit<T>` has the same size/alignment/ABI as `T`, so the cast pointer is
        // valid for the same writes; `ptr::write` never reads (and so never drops) the
        // destination, so it being uninitialized is fine
        unsafe { self.cast::<T>().write(value) }
    }
}
