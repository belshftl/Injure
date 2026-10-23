// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use self_cell::self_cell;
use unicode_bidi::utf16::BidiInfo;

self_cell!(
    /// An owned copy of a UTF-16 string analyzed by the Unicode Bidirectional Algorithm (UAX #9).
    /// The string is split into paragraphs after each character of bidirectional class B
    /// (paragraph separator) as per UAX #9 rule P1, and each paragraph gets its direction from its
    /// first strong character, or LTR if it has none. All indices are in UTF-16 code units.
    ///
    /// # ABI
    ///
    /// Opaque; only ever created by the native side, and passed through the FFI boundary as a
    /// pointer.
    pub struct BidiText {
        owner: Box<[u16]>,
        #[covariant]
        dependent: BidiInfo,
    }
);

/// A run of text at a single bidirectional level within a line.
///
/// # ABI
///
/// Has a size of 12 bytes and an alignment of 4 bytes:
///   1. 4 bytes (start)
///   2. 4 bytes (len)
///   3. 4 bytes (rtl)
#[repr(C)]
#[derive(Debug, Clone)]
pub struct BidiRun {
    pub start: u32,
    pub len: u32,
    pub rtl: u32,
}

const _: () = {
    assert!(size_of::<BidiRun>() == 12);

    assert!(std::mem::align_of::<BidiRun>() == 4);

    assert!(std::mem::offset_of!(BidiRun, start) == 0);
    assert!(std::mem::offset_of!(BidiRun, len) == 4);
    assert!(std::mem::offset_of!(BidiRun, rtl) == 8);
};

impl BidiText {
    pub fn from_utf16(text: &[u16]) -> Self {
        BidiText::new(Box::from(text), |t| BidiInfo::new(t, None))
    }

    pub fn text(&self) -> &[u16] {
        self.borrow_owner()
    }

    pub fn info(&self) -> &BidiInfo<'_> {
        self.borrow_dependent()
    }

    pub fn len(&self) -> usize {
        self.text().len()
    }

    pub fn levels(&self, out: &mut [u8]) {
        for (o, l) in out.iter_mut().zip(&self.info().levels) {
            *o = l.number();
        }
    }

    pub fn visual_runs(&self, start: usize, limit: usize, out: &mut [BidiRun]) -> usize {
        let mut n = 0;
        for para in &self.info().paragraphs {
            let s = start.max(para.range.start);
            let e = limit.min(para.range.end);
            if s >= e {
                continue;
            }
            let (levels, runs) = self.info().visual_runs(para, s..e);
            for run in runs {
                if let Some(slot) = out.get_mut(n) {
                    *slot = BidiRun {
                        start: count(run.start),
                        len: count(run.end - run.start),
                        rtl: u32::from(levels[run.start].is_rtl()),
                    };
                }
                n += 1;
            }
        }
        n
    }
}

#[track_caller]
fn count(len: usize) -> u32 {
    u32::try_from(len).expect("ijt_bidi_open should have rejected strings longer than u32::MAX")
}
