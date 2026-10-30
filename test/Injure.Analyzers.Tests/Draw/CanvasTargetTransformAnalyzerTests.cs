// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Analyzers.Draw;

namespace Injure.Analyzers.Tests.Draw;

public sealed class CanvasTargetTransformAnalyzerTests {
	private static string[] reported(string body) {
		string source = $$"""
			using System.Numerics;
			using Injure.Draw;
			public sealed class C {
				private static void M(Canvas cv, CanvasTarget t, CanvasParamsOverride existing) {
					{{body}}
				}
			}
			""";
		return Gen.Analyze(new CanvasTargetTransformAnalyzer(), source)
			.Where(static d => d.Id == "IJ0300")
			.Select(static d => d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan))
			.ToArray();
	}

	[Theory]
	[InlineData("cv.PushParams(target: t);", "target: t")]
	[InlineData("cv.PushParams(t);", "t")]
	[InlineData("cv.PushParams(target: t, scissor: CanvasScissorOverride.NoScissor);", "target: t")]
	[InlineData("var o = new CanvasParamsOverride(Target: t);", "Target: t")]
	[InlineData("CanvasParamsOverride o = new(Target: CanvasTarget.Primary(Viewport.Whole(Vector2.One), CanvasLoadOp.Load));", "Target: CanvasTarget.Primary(Viewport.Whole(Vector2.One), CanvasLoadOp.Load)")]
	[InlineData("var o = new CanvasParamsOverride { Target = t };", "Target = t")]
	[InlineData("var o = new CanvasParamsOverride(Target: default(CanvasTarget));", "Target: default(CanvasTarget)")]
	public static void TargetChangesWithoutTheTransformAreReported(string body, string at) =>
		Assert.Equal(at, Assert.Single(reported(body)));

	[Theory]
	[InlineData("cv.PushParams(target: t, transform: null);")]
	[InlineData("cv.PushParams(target: t, transform: CanvasTransformOverride.Set(Matrix3x2.Identity));")]
	[InlineData("var o = new CanvasParamsOverride(Target: t, Transform: null);")]
	[InlineData("var o = new CanvasParamsOverride(Target: t) { Transform = null };")]
	[InlineData("var o = new CanvasParamsOverride { Target = t, Transform = CanvasTransformOverride.Multiply(Matrix3x2.Identity) };")]
	[InlineData("cv.PushParams(target: null);")]
	[InlineData("cv.PushParams(target: default);")]
	[InlineData("var o = new CanvasParamsOverride(Target: null);")]
	[InlineData("cv.PushParams(scissor: CanvasScissorOverride.NoScissor);")]
	[InlineData("var o = existing with { Target = t };")]
	[InlineData("cv.PushParams(existing);")]
	public static void OtherOverridesAreFine(string body) => Assert.Empty(reported(body));
}
