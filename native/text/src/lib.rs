// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

#![deny(unsafe_op_in_unsafe_fn)]
#![deny(clippy::borrow_as_ptr)]
#![warn(clippy::pedantic)]

mod bidi;
mod linebreak;

use std::mem::MaybeUninit;
use std::slice;

use bidi::BidiText;
use linebreak::BreakKind;

#[repr(u32)]
pub enum IjtResult {
    Ok = 0,
    InvalidArgument = 1,
}

unsafe fn slice_or_empty<'a, T>(p: *const T, len: usize) -> Option<&'a [T]> {
    if len == 0 {
        return Some(&[]);
    }
    if p.is_null() {
        return None;
    }
    Some(unsafe { slice::from_raw_parts(p, len) })
}

unsafe fn slice_or_empty_mut<'a, T>(p: *mut T, len: usize) -> Option<&'a mut [T]> {
    if len == 0 {
        return Some(&mut []);
    }
    if p.is_null() {
        return None;
    }
    Some(unsafe { slice::from_raw_parts_mut(p, len) })
}

/// UAX #14 line breaking of a `len` UTF-16 code units long UTF-16 string `text`.
///
/// `kinds[i]` is set to the `ijt_break_kind` at position `i`, i.e. between `text[i - 1]` and
/// `text[i]`. Since `text` is of length `len`, there are `len + 1` positions. Position 0 is never a
/// break, and position `len` is a break only after a mandatory break character. All positions are
/// in UTF-16 code units.
///
/// `language` is a `language_len` UTF-8 code units (i.e. bytes) long UTF-8 BCP 47 tag and is not
/// null-terminated. Its `-u-lb-` keyword (loose, normal, strict) selects the strictness, and its
/// `-u-lw-` keyword (normal, breakall, keepall) the word option. A malformed tag gets the default
/// options rather than being rejected.
///
/// # Safety
///
/// `text` must be readable for `len` **UTF-16 code units** (not bytes), `language` must be readable
/// for `language_len` bytes, and `kinds` must be writable for `len + 1` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ijt_line_breaks(
    text: *const u16,
    len: usize,
    language: *const u8,
    language_len: usize,
    kinds: *mut MaybeUninit<BreakKind>,
) -> IjtResult {
    let (Some(text), Some(language), Some(kinds)) = (
        unsafe { slice_or_empty(text, len) },
        unsafe { slice_or_empty(language, language_len) },
        unsafe { slice_or_empty_mut(kinds, len + 1) },
    ) else {
        return IjtResult::InvalidArgument;
    };
    // a tag that isn't valid utf-8 can't be a valid bcp 47 tag either
    linebreak::line_breaks(text, std::str::from_utf8(language).unwrap_or(""), kinds);
    IjtResult::Ok
}

/// Analyzes a `len` UTF-16 code units long UTF-16 string `text`, writing a new paragraph handle
/// into `out`. The string pointed to by `text` is copied, and as such `text` need only remain
/// valid for the duration of the call. On failure, `out` is not written into.
///
/// `len` must be at most `u32::MAX`; otherwise, this function fails with
/// [`IjtResult::InvalidArgument`].
///
/// The handle must be passed to [`ijt_bidi_close`] exactly once when no longer needed.
///
/// # Safety
///
/// `text` must be readable for `len` **UTF-16 code units** (not bytes), and `out` must be writable
/// for one pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ijt_bidi_open(
    text: *const u16,
    len: usize,
    out: *mut *mut BidiText,
) -> IjtResult {
    if out.is_null() || u32::try_from(len).is_err() {
        return IjtResult::InvalidArgument;
    }
    let Some(text) = (unsafe { slice_or_empty(text, len) }) else {
        return IjtResult::InvalidArgument;
    };
    unsafe { *out = Box::into_raw(Box::new(BidiText::from_utf16(text))) };
    IjtResult::Ok
}

/// Writes the resolved embedding level of each code unit of the analyzed string into `levels`. The
/// levels are as of the end of UAX #9 rule I2, i.e. before the per-line adjustments of rule L1.
/// Each level is at most 126, and odd levels are RTL.
///
/// `len` must equal the length of the analyzed string; otherwise, this function fails with
/// [`IjtResult::InvalidArgument`].
///
/// # Safety
///
/// `bidi` must be a handle obtained from [`ijt_bidi_open`] that has not been closed, and `levels`
/// must be writable for `len` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ijt_bidi_levels(
    bidi: *const BidiText,
    levels: *mut u8,
    len: usize,
) -> IjtResult {
    let Some(bidi) = (unsafe { bidi.as_ref() }) else {
        return IjtResult::InvalidArgument;
    };
    let Some(levels) = (unsafe { slice_or_empty_mut(levels, len) }) else {
        return IjtResult::InvalidArgument;
    };
    if len != bidi.len() {
        return IjtResult::InvalidArgument;
    }
    bidi.levels(levels);
    IjtResult::Ok
}

/// Reorders the line `[line_start, line_limit)` of the analyzed string as per UAX #9 rules L1 and
/// L2, writing its runs into `runs` in visual order (left to right) and the total amount of runs
/// into `count`. Only the first `cap` runs are written, so if `count` exceeds `cap`, the caller
/// must call this again with a `cap` of at least `count` to get every run. `count` is at most
/// `line_limit - line_start`, and is 0 if and only if the line is empty.
///
/// The runs cover the line exactly. A line that spans several paragraphs has the runs of each
/// paragraph in logical order of the paragraphs.
///
/// `line_start` must be at most `line_limit`, and `line_limit` must be at most the length of the
/// analyzed string; otherwise, this function fails with [`IjtResult::InvalidArgument`].
///
/// # Safety
///
/// `bidi` must be a handle obtained from [`ijt_bidi_open`] that has not been closed, `runs` must
/// be writable for `cap` **elements** (not bytes), and `count` must be writable for one `usize`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ijt_bidi_visual_runs(
    bidi: *const BidiText,
    line_start: usize,
    line_limit: usize,
    runs: *mut bidi::BidiRun,
    cap: usize,
    count: *mut usize,
) -> IjtResult {
    let Some(bidi) = (unsafe { bidi.as_ref() }) else {
        return IjtResult::InvalidArgument;
    };
    let Some(runs) = (unsafe { slice_or_empty_mut(runs, cap) }) else {
        return IjtResult::InvalidArgument;
    };
    if count.is_null() || line_start > line_limit || line_limit > bidi.len() {
        return IjtResult::InvalidArgument;
    }
    unsafe { *count = bidi.visual_runs(line_start, line_limit, runs) };
    IjtResult::Ok
}

/// Frees a [`BidiText`] handle. Does nothing if `bidi` is null.
///
/// # Safety
///
/// `bidi` must be null or a handle obtained from [`ijt_bidi_open`] that has not been closed. The
/// handle must not be used after this call.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ijt_bidi_close(bidi: *mut BidiText) {
    if !bidi.is_null() {
        drop(unsafe { Box::from_raw(bidi) });
    }
}
