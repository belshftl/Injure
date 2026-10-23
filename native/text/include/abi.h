/*
 * SPDX-FileCopyrightText: 2026 belshftl
 * SPDX-License-Identifier: MIT
 */

/*
 * This header file is normative. Any deviation between this header and the actual exports or
 * behavior of the native library is a bug.
 */

#pragma once

#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((__visibility__("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/*
 * ================================================================================================
 * types
 */
typedef enum IjtResult : uint32_t {
	IJT_OK = 0,
	IJT_INVALID_ARGUMENT = 1, /* a null pointer with a nonzero length, or an out-of-range index/length */
} IjtResult;

typedef enum BreakKind : uint8_t {
	IJT_BREAK_NONE = 0,
	IJT_BREAK_SOFT = 1, /* a line may break here */
	IJT_BREAK_HARD = 2, /* a line must break here */
} BreakKind;

/*
 * An owned copy of a UTF-16 string analyzed by the Unicode Bidirectional Algorithm (UAX #9). The
 * string is split into paragraphs after each character of bidirectional class B (paragraph
 * separator) as per UAX #9 rule P1, and each paragraph gets its direction from its first strong
 * character, or LTR if it has none. All indices are in UTF-16 code units.
 *
 * # ABI
 *
 * Opaque; only ever created by the native side, and passed through the FFI boundary as a pointer.
 */
typedef struct BidiText BidiText;

/*
 * A run of text at a single bidirectional level within a line.
 *
 * # ABI
 *
 * Has a size of 12 bytes and an alignment of 4 bytes:
 *   1. 4 bytes (start)
 *   2. 4 bytes (len)
 *   3. 4 bytes (rtl)
 */
typedef struct BidiRun {
	uint32_t start; /* index of the first code unit */
	uint32_t len; /* in code units; never 0 */
	uint32_t rtl; /* 1 if the run is RTL, 0 otherwise */
} BidiRun;

/*
 * ================================================================================================
 * line breaking
 */

/*
 * UAX #14 line breaking of a `len` UTF-16 code units long UTF-16 string `text`.
 *
 * `kinds[i]` is set to the `ijt_break_kind` at position `i`, i.e. between `text[i - 1]` and
 * `text[i]`. Since `text` is of length `len`, there are `len + 1` positions. Position 0 is never a
 * break, and position `len` is a break only after a mandatory break character. All positions are
 * in UTF-16 code units.
 *
 * `language` is a `language_len` UTF-8 code units (i.e. bytes) long UTF-8 BCP 47 tag and is not
 * null-terminated. Its `-u-lb-` keyword (loose, normal, strict) selects the strictness, and its
 * `-u-lw-` keyword (normal, breakall, keepall) the word option. A malformed tag gets the default
 * options rather than being rejected.
 *
 * # Safety
 *
 * `text` must be readable for `len` **UTF-16 code units** (not bytes), `language` must be readable
 * for `language_len` bytes, and `kinds` must be writable for `len + 1` bytes.
 */
EXPORT IjtResult ijt_line_breaks(
	const uint16_t *text,
	size_t len,
	const uint8_t *language,
	size_t language_len,
	BreakKind *kinds
);

/*
 * ================================================================================================
 * bidi
 */

/*
 * Analyzes a `len` UTF-16 code units long UTF-16 string `text`, writing a new paragraph handle
 * into `out`. The string pointed to by `text` is copied, and as such `text` need only remain
 * valid for the duration of the call. On failure, `out` is not written into.
 *
 * `len` must be at most `UINT32_MAX`; otherwise, this function fails with `IJT_INVALID_ARGUMENT`.
 *
 * The handle must be passed to `ijt_bidi_close` exactly once when no longer needed.
 *
 * # Safety
 *
 * `text` must be readable for `len` **UTF-16 code units** (not bytes), and `out` must be writable
 * for one pointer.
 */
EXPORT IjtResult ijt_bidi_open(const uint16_t *text, size_t len, BidiText **out);

/*
 * Writes the resolved embedding level of each code unit of the analyzed string into `levels`. The
 * levels are as of the end of UAX #9 rule I2, i.e. before the per-line adjustments of rule L1.
 * Each level is at most 126, and odd levels are RTL.
 *
 * `len` must equal the length of the analyzed string; otherwise, this function fails with
 * `IJT_INVALID_ARGUMENT`.
 *
 * # Safety
 *
 * `bidi` must be a handle obtained from `ijt_bidi_open` that has not been closed, and `levels`
 * must be writable for `len` bytes.
 */
EXPORT IjtResult ijt_bidi_levels(const BidiText *bidi, uint8_t *levels, size_t len);

/*
 * Reorders the line `[line_start, line_limit)` of the analyzed string as per UAX #9 rules L1 and
 * L2, writing its runs into `runs` in visual order (left to right) and the total amount of runs
 * into `count`. Only the first `cap` runs are written, so if `count` exceeds `cap`, the caller
 * must call this again with a `cap` of at least `count` to get every run. `count` is at most
 * `line_limit - line_start`, and is 0 if and only if the line is empty.
 *
 * The runs cover the line exactly. A line that spans several paragraphs has the runs of each
 * paragraph in logical order of the paragraphs.
 *
 * `line_start` must be at most `line_limit`, and `line_limit` must be at most the length of the
 * analyzed string; otherwise, this function fails with `IJT_INVALID_ARGUMENT`.
 *
 * # Safety
 *
 * `bidi` must be a handle obtained from `ijt_bidi_open` that has not been closed, `runs` must be
 * writable for `cap` **elements** (not bytes), and `count` must be writable for one `size_t`.
 */
EXPORT IjtResult ijt_bidi_visual_runs(
	const BidiText *bidi,
	size_t line_start,
	size_t line_limit,
	BidiRun *runs,
	size_t cap,
	size_t *count
);

/*
 * Frees a `BidiText` handle. Does nothing if `bidi` is null.
 *
 * # Safety
 *
 * `bidi` must be null or a handle obtained from `ijt_bidi_open` that has not been closed. The
 * handle must not be used after this call.
 */
EXPORT void ijt_bidi_close(BidiText *bidi);

#ifdef __cplusplus
}
#endif
