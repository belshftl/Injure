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

    /// Copies `src` into the `src.len()` elements starting at the pointer, without reading or
    /// dropping their old contents.
    ///
    /// # Safety
    ///
    /// The pointer must be valid for writes of `src.len()` elements and properly aligned, and the
    /// region of memory beginning at the pointer with a size of `src.len()` elements must not
    /// overlap `src`, as for [`std::ptr::copy_nonoverlapping`].
    unsafe fn write_slice(self, src: &[T])
    where
        T: Copy;
}

impl<T> UninitPtr<T> for *mut MaybeUninit<T> {
    unsafe fn write_value(self, value: T) {
        // SAFETY: `MaybeUninit<T>` has the same size/alignment/ABI as `T`, so the cast pointer is
        // valid for the same writes; `ptr::write` never reads (and so never drops) the
        // destination, so it being uninitialized is fine
        unsafe { self.cast::<T>().write(value) }
    }

    unsafe fn write_slice(self, src: &[T])
    where
        T: Copy,
    {
        // SAFETY: `MaybeUninit<T>` has the same size/alignment/ABI as `T`, so the cast pointer is
        // valid for the same writes; `ptr::copy_nonoverlapping` never reads (and so never drops
        // anything from) the destination, so it being uninitialized is fine, and a bitwise copy is
        // valid to perform since `T` is `Copy`
        unsafe { std::ptr::copy_nonoverlapping(src.as_ptr(), self.cast::<T>(), src.len()) }
    }
}
