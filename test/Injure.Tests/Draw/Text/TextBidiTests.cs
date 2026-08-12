// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using HarfBuzzSharp;
using Injure.Draw.Text;
using static Injure.Tests.Draw.Text.Util;

namespace Injure.Tests.Draw.Text;

public sealed class TextBidiTests {
	[Fact]
	public static void LogicalRunsPureLtrWorks() {
		LogicalBidiRun[] runs = TextAnalysis.GetLogicalBidiRuns("abc");
		AssertLogicalRuns(runs, (0, "abc".Length, Direction.LeftToRight));
	}

	[Fact]
	public static void LogicalRunsPureRtlWorks() {
		LogicalBidiRun[] runs = TextAnalysis.GetLogicalBidiRuns("אבג");
		AssertLogicalRuns(runs, (0, "אבג".Length, Direction.RightToLeft));
	}

	[Fact]
	public static void LogicalRunsMixedDirWorks() {
		LogicalBidiRun[] runs = TextAnalysis.GetLogicalBidiRuns("abc אבג def");
		AssertLogicalRuns(
			runs,
			(0, "abc ".Length, Direction.LeftToRight),
			("abc ".Length, "אבג".Length, Direction.RightToLeft),
			("abc אבג".Length, " def".Length, Direction.LeftToRight)
		);
	}

	[Fact]
	public static void VisualRunsPureLtrWorks() {
		VisualBidiRun[] runs = TextAnalysis.GetVisualBidiRunsForLine("abc", 0, "abc".Length);
		AssertVisualRuns(runs, (0, "abc".Length, Direction.LeftToRight));
	}

	[Fact]
	public static void VisualRunsPureRtlWorks() {
		VisualBidiRun[] runs = TextAnalysis.GetVisualBidiRunsForLine("אבג", 0, "אבג".Length);
		AssertVisualRuns(runs, (0, "אבג".Length, Direction.RightToLeft));
	}

	[Fact]
	public static void VisualRunsMixedDirWorks() {
		VisualBidiRun[] runs = TextAnalysis.GetVisualBidiRunsForLine("abc אבג def", 0, "abc אבג def".Length);
		AssertVisualRuns(
			runs,
			(0, "abc ".Length, Direction.LeftToRight),
			("abc ".Length, "אבג".Length, Direction.RightToLeft),
			("abc אבג".Length, " def".Length, Direction.LeftToRight)
		);
	}

	[Fact]
	public static void VisualRunsSubrangeUsesAbsoluteIndices() {
		const string text = "abc אבג def";
		const int lineStart = 4;
		const int lineLimit = 7;
		VisualBidiRun[] runs = TextAnalysis.GetVisualBidiRunsForLine(text, lineStart, lineLimit);
		Assert.NotEmpty(runs);
		Assert.All(runs, run => Assert.InRange(run.Start, lineStart, lineLimit));
	}
}
