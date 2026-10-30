// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Primitives;

namespace Injure.Tests.Draw;

public static class CanvasParamsMergeTests {
	private static readonly CanvasParams baseParams = CanvasParams.Primary(Viewport.Whole(new Vector2(8, 8)), CanvasLoadOp.Clear(SrgbStraight32.Black));

	[Fact]
	public static void EmptyOverrideKeepsEverything() {
		Assert.Equal(baseParams, baseParams.Merge(default));
	}

	[Fact]
	public static void TransformSetReplacesAndMultiplyAppliesFirst() {
		var scale = Matrix3x2.CreateScale(2);
		var translate = Matrix3x2.CreateTranslation(1, 0);
		CanvasParams scaled = baseParams.Merge(new(Transform: CanvasTransformOverride.Set(scale)));
		Assert.Equal(scale, scaled.Transform);
		Assert.Equal(translate, scaled.Merge(new(Transform: CanvasTransformOverride.Set(translate))).Transform);
		// translate first, then scale: (1, 0) -> (2, 0) -> (4, 0)
		CanvasParams nested = scaled.Merge(new(Transform: CanvasTransformOverride.Multiply(translate)));
		Assert.Equal(new Vector2(4, 0), Vector2.Transform(new Vector2(1, 0), nested.Transform));
	}

	[Fact]
	public static void ScissorOverridesSetIntersectAndClear() {
		RectF a = new(0, 0, 4, 4);
		RectF b = new(2, 1, 4, 4);
		// intersecting with no scissor sets it
		CanvasParams withA = baseParams.Merge(new(Scissor: CanvasScissorOverride.Intersect(a)));
		Assert.Equal(CanvasScissor.Set(a), withA.Scissor);
		Assert.Equal(CanvasScissor.Set(new RectF(2, 1, 2, 3)), withA.Merge(new(Scissor: CanvasScissorOverride.Intersect(b))).Scissor);
		Assert.Equal(CanvasScissor.Set(b), withA.Merge(new(Scissor: CanvasScissorOverride.Set(b))).Scissor);
		Assert.Equal(CanvasScissor.None, withA.Merge(new(Scissor: CanvasScissorOverride.NoScissor)).Scissor);
	}

	[Fact]
	public static void DisjointScissorIntersectionsAreEmpty() {
		CanvasParams withA = baseParams.Merge(new(Scissor: CanvasScissorOverride.Set(new RectF(0, 0, 2, 2))));
		CanvasParams far = withA.Merge(new(Scissor: CanvasScissorOverride.Intersect(new RectF(5, 5, 2, 2))));
		CanvasParams touching = withA.Merge(new(Scissor: CanvasScissorOverride.Intersect(new RectF(2, 0, 2, 2))));
		Assert.Equal(CanvasScissor.Set(RectF.Empty), far.Scissor);
		Assert.Equal(far.Scissor, touching.Scissor);
	}

	[Fact]
	public static void EffectiveTransformAppliesTheViewportLast() {
		Viewport vp = new(new Vector2(3, 0), 2f, new Vector2(4, 4));
		CanvasParams p = CanvasParams.Primary(vp, CanvasLoadOp.Clear(SrgbStraight32.Black))
			.Merge(new(Transform: CanvasTransformOverride.Set(Matrix3x2.CreateTranslation(1, 0))));
		// translated by 1, then scaled by 2 and offset by 3
		Assert.Equal(new Vector2(5, 0), Vector2.Transform(Vector2.Zero, p.EffectiveTransform));
		Assert.Throws<InvalidDefaultException>(() => default(CanvasParams).EffectiveTransform);
	}

	[Fact]
	public static void MergeDoesntValidateTheResult() {
		// the product's inverse overflows, which PushParams would reject
		var tiny = Matrix3x2.CreateScale(1e-10f);
		CanvasParams p = baseParams.Merge(new(Transform: CanvasTransformOverride.Set(tiny)));
		Assert.Equal(tiny * tiny, p.Merge(new(Transform: CanvasTransformOverride.Multiply(tiny))).Transform);
	}

	[Fact]
	public static void DefaultTransformOverridesAreRejected() {
		Assert.Throws<ArgumentInvalidDefaultException>(() => baseParams.Merge(new(Transform: default(CanvasTransformOverride))));
	}

	[Fact]
	public static void DefaultScissorOverridesAreRejected() {
		Assert.Throws<ArgumentInvalidDefaultException>(() => baseParams.Merge(new(Scissor: default(CanvasScissorOverride))));
		Assert.Throws<InvalidDefaultException>(() => default(CanvasScissorOverride).Kind);
		Assert.Throws<InvalidDefaultException>(() => default(CanvasScissorOverride).Rect);
		// a null override still means no change
		Assert.Equal(baseParams.Scissor, baseParams.Merge(new(Scissor: null)).Scissor);
	}
}
