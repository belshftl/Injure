// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;
using static Injure.Tests.Draw.Text.Util;

namespace Injure.Tests.Draw.Text;

[Collection("needs_native_libs")]
public static class TextAnalysisTests {
	public static readonly TheoryData<string, (int Start, int Length)[]> GraphemeCases = new() {
		{ "a", new[] { (0, 1) } },
		{ "ab", new[] { (0, 1), (1, 1) } },
		{ "e\u0301", new[] { (0, 2) } },
		{ "A\u030A", new[] { (0, 2) } },
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
	[InlineData("a😀b", "a😀b")]
	[InlineData("a\uD800b", "a�b")]
	[InlineData("a\uDC00", "a�")]
	[InlineData("\uD800", "�")]
	[InlineData("\uDC00\uD800", "��")]
	[InlineData("\uD800\uD800\uDC00", "�\uD800\uDC00")]
	public static void ReplaceUnpairedSurrogatesWorks(string text, string expected) =>
		Assert.Equal(expected, TextAnalysis.ReplaceUnpairedSurrogates(text));

	[Fact]
	public static void ReplaceUnpairedSurrogatesReturnsValidTextItself() {
		string s = "abc😀";
		Assert.Same(s, TextAnalysis.ReplaceUnpairedSurrogates(s));
	}

	private static int[] breakPositions(string text, string language) =>
		TextAnalysis.GetLineBreaks(text, language).Select(static b => b.Position).ToArray();

	[Fact]
	public static void GetLineBreaksWorks() {
		LineBreakOpportunity[] breaks = TextAnalysis.GetLineBreaks("abc def ghi", "en");
		AssertBreaks(breaks, (4, LineBreakKind.Soft), (8, LineBreakKind.Soft));
	}

	[Fact]
	public static void GetLineBreaksHandlesNbsp() =>
		Assert.Empty(TextAnalysis.GetLineBreaks("abc\u00A0def", "en"));

	[Fact]
	public static void MandatoryBreaksAreHard() {
		AssertBreaks(TextAnalysis.GetLineBreaks("a\u2028b", "und"), (2, LineBreakKind.Hard));
		// no break between CR and LF, and a mandatory break at the end of the text counts
		AssertBreaks(TextAnalysis.GetLineBreaks("a\r\nb\r", "und"), (3, LineBreakKind.Hard), (5, LineBreakKind.Hard));
	}

	[Fact]
	public static void ThaiBreaksBetweenWords() =>
		Assert.Equal([3, 6, 11, 15, 18, 21, 24, 29, 32], breakPositions("ฉันกินกระจกได้ แต่มันไม่ทำให้ฉันเจ็บ", "th"));

	[Fact]
	public static void KeepAllOnlyBreaksKoreanBetweenWords() {
		const string text = "한국어 텍스트";
		Assert.Equal([1, 2, 4, 5, 6], breakPositions(text, "ko"));
		Assert.Equal([4], breakPositions(text, "ko-u-lw-keepall"));
	}

	[Fact]
	public static void LineBreakStrictnessWorks() {
		const string text = "あゃ";
		Assert.Empty(breakPositions(text, "ja")); // strict is the default
		Assert.Empty(breakPositions(text, "ja-u-lb-strict"));
		Assert.Equal([1], breakPositions(text, "ja-u-lb-normal"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("-")]
	[InlineData("not a tag")]
	[InlineData("ko-u-lw")]
	[InlineData("ko-u-lw-bogus")]
	public static void MalformedLanguageTagsGetTheDefaults(string language) {
		const string text = "한국어 텍스트";
		Assert.Equal(breakPositions(text, "und"), breakPositions(text, language));
	}
}
