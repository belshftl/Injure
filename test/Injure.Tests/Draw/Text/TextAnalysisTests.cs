// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;
using static Injure.Tests.Draw.Text.Util;

namespace Injure.Tests.Draw.Text;

[Collection("needs_libmisc")]
public static class TextAnalysisTests {
	public static readonly TheoryData<string, (int Start, int Length)[]> GraphemeCases = new() {
		{ "a", new[] { (0, 1) } },
		{ "ab", new[] { (0, 1), (1, 1) } },
		{ "e\u0301", new[] { (0, 2) } },
		{ "A\u030a", new[] { (0, 2) } },
		{ "x\r\ny", new[] { (0, 1), (1, 2), (3, 1) } },
	};

	[Theory]
	[MemberData(nameof(GraphemeCases))]
	public static void GetGraphemeSpansWorks(string text, (int Start, int Length)[] expected) {
		GraphemeSpan[] spans = TextAnalysis.GetGraphemeSpans(text);
		AssertSpans(spans, expected);
	}

	[Theory]
	[InlineData("abc", "abc")]
	[InlineData("a\ud83d\ude00b", "a\ud83d\ude00b")]
	[InlineData("a\ud800b", "a\ufffdb")]
	[InlineData("a\udc00", "a\ufffd")]
	[InlineData("\ud800", "\ufffd")]
	[InlineData("\udc00\ud800", "\ufffd\ufffd")]
	[InlineData("\ud800\ud800\udc00", "\ufffd\ud800\udc00")]
	public static void ReplaceUnpairedSurrogatesWorks(string text, string expected) =>
		Assert.Equal(expected, TextAnalysis.ReplaceUnpairedSurrogates(text));

	[Fact]
	public static void ReplaceUnpairedSurrogatesReturnsValidTextItself() {
		string s = "abc\uD83D\uDE00";
		Assert.Same(s, TextAnalysis.ReplaceUnpairedSurrogates(s));
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData("", null)]
	[InlineData("en", "en")]
	[InlineData("EN-us", "en")]
	[InlineData("zh-Hant-TW", "zh")]
	[InlineData("und", null)]
	// three-letter subtags would otherwise get truncated to the first two
	[InlineData("kok", null)]
	[InlineData("ja-u-lb-strict", "ja-strict")]
	[InlineData("ja-JP-u-ca-japanese-lb-strict", "ja-strict")]
	[InlineData("und-u-lb-strict", "und-strict")]
	[InlineData("ja-u-lb-loose", "ja")]
	// lb-strict only counts within the u extension
	[InlineData("ja-x-lb-strict", "ja")]
	[InlineData("ja-u-ca-gregory-x-lb-strict", "ja")]
	public static void UnibreakLanguageWorks(string? tag, string? expected) =>
		Assert.Equal(expected, TextAnalysis.UnibreakLanguage(tag));

	[Fact]
	public static void LanguageSpecificLineBreakingWorks() {
		// in libunibreak, by default, '«' and '»' are treated as ambiguous quotation marks, but setting
		// the language to french treats them as opening and closing punctuation respectively, so the
		// spaces inside them don't allow breaks
		string text = "a \u00ab b \u00bb c";
		Assert.Equal([2, 4, 6, 8], TextAnalysis.GetLineBreaks(text, null).Select(static b => b.Position));
		Assert.Equal([2, 8], TextAnalysis.GetLineBreaks(text, TextAnalysis.UnibreakLanguage("fr")).Select(static b => b.Position));
	}

	[Fact]
	public static void StrictLineBreakingWorks() {
		// 'ゃ', U+3083 HIRAGANA LETTER SMALL YA; small kana is forbidden from starting a line in strict
		string text = "\u3042\u3083";
		Assert.Contains(TextAnalysis.GetLineBreaks(text, TextAnalysis.UnibreakLanguage("ja")), static b => b.Position == 1);
		Assert.DoesNotContain(TextAnalysis.GetLineBreaks(text, TextAnalysis.UnibreakLanguage("ja-u-lb-strict")), static b => b.Position == 1);
	}

	[Fact]
	public static void GetLineBreaksWorks() {
		LineBreakOpportunity[] breaks = TextAnalysis.GetLineBreaks("abc def ghi", unibreakLanguage: "en");
		AssertBreaks(breaks, (4, LineBreakKind.Soft), (8, LineBreakKind.Soft));
	}

	[Fact]
	public static void GetLineBreaksHandlesNbsp() {
		LineBreakOpportunity[] breaks = TextAnalysis.GetLineBreaks("abc\u00a0def", unibreakLanguage: "en");
		Assert.Empty(breaks);
	}
}
