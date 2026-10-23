// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use icu_locale_core::Locale;
use icu_locale_core::extensions::unicode::key;
use icu_properties::CodePointMapData;
use icu_properties::props::LineBreak;
use icu_segmenter::LineSegmenter;
use icu_segmenter::options::{LineBreakOptions, LineBreakStrictness, LineBreakWordOption};
use std::mem::MaybeUninit;

#[repr(u8)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BreakKind {
    None = 0,
    Soft = 1,
    Hard = 2,
}

pub fn line_breaks(text: &[u16], language: &str, kinds: &mut [MaybeUninit<BreakKind>]) {
    for k in kinds.iter_mut() {
        k.write(BreakKind::None);
    }
    let locale = Locale::try_from_str(language).ok();
    let mut options = LineBreakOptions::default();
    if let Some(locale) = &locale {
        let keywords = &locale.extensions.unicode.keywords;
        options.strictness =
            keywords
                .get(&key!("lb"))
                .and_then(|v| match v.as_single_subtag()?.as_str() {
                    "loose" => Some(LineBreakStrictness::Loose),
                    "normal" => Some(LineBreakStrictness::Normal),
                    "strict" => Some(LineBreakStrictness::Strict),
                    _ => None,
                });
        options.word_option =
            keywords
                .get(&key!("lw"))
                .and_then(|v| match v.as_single_subtag()?.as_str() {
                    "normal" => Some(LineBreakWordOption::Normal),
                    "breakall" => Some(LineBreakWordOption::BreakAll),
                    "keepall" => Some(LineBreakWordOption::KeepAll),
                    _ => None,
                });
        options.content_locale = Some(&locale.id);
    }
    let segmenter = LineSegmenter::new_dictionary(options);
    let classes = CodePointMapData::<LineBreak>::new();
    for p in segmenter.segment_utf16(text) {
        if p == 0 {
            continue;
        }
        let mandatory = matches!(
            classes.get32(last_code_point(&text[..p])),
            LineBreak::MandatoryBreak
                | LineBreak::CarriageReturn
                | LineBreak::LineFeed
                | LineBreak::NextLine
        );
        if p == text.len() && !mandatory {
            continue;
        }
        kinds[p].write(if mandatory {
            BreakKind::Hard
        } else {
            BreakKind::Soft
        });
    }
}

fn last_code_point(text: &[u16]) -> u32 {
    let last = text[text.len() - 1];
    if (0xdc00..0xe000).contains(&last) && text.len() >= 2 {
        let high = text[text.len() - 2];
        if (0xd800..0xdc00).contains(&high) {
            return 0x10000 + ((u32::from(high) - 0xd800) << 10) + (u32::from(last) - 0xdc00);
        }
    }
    u32::from(last)
}
