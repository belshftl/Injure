// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Primitives;

namespace Injure.Tests.Draw;

public sealed class CanvasParamsMergeTests {
	private static readonly CanvasParams baseParams = CanvasParams.Primary(Matrix3x2.Identity, SrgbColor32.Black);

	[Fact]
	public void EmptyOverrideKeepsEverything() {
		Assert.Equal(baseParams, baseParams.Merge(default));
	}

	[Fact]
	public void TransformSetReplacesAndMultiplyAppliesFirst() {
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
	public void ScissorOverridesSetIntersectAndClear() {
		RectI a = new(0, 0, 4, 4);
		RectI b = new(2, 1, 4, 4);
		// intersecting with no scissor sets it
		CanvasParams withA = baseParams.Merge(new(Scissor: CanvasScissorOverride.Intersect(a)));
		Assert.Equal(CanvasScissor.Set(a), withA.Scissor);
		Assert.Equal(CanvasScissor.Set(new RectI(2, 1, 2, 3)), withA.Merge(new(Scissor: CanvasScissorOverride.Intersect(b))).Scissor);
		Assert.Equal(CanvasScissor.Set(b), withA.Merge(new(Scissor: CanvasScissorOverride.Set(b))).Scissor);
		Assert.Equal(CanvasScissor.None, withA.Merge(new(Scissor: CanvasScissorOverride.None)).Scissor);
	}

	[Fact]
	public void DisjointScissorIntersectionsAreEmpty() {
		CanvasParams withA = baseParams.Merge(new(Scissor: CanvasScissorOverride.Set(new RectI(0, 0, 2, 2))));
		CanvasParams far = withA.Merge(new(Scissor: CanvasScissorOverride.Intersect(new RectI(5, 5, 2, 2))));
		CanvasParams touching = withA.Merge(new(Scissor: CanvasScissorOverride.Intersect(new RectI(2, 0, 2, 2))));
		Assert.Equal(CanvasScissor.Set(RectI.Empty), far.Scissor);
		Assert.Equal(far.Scissor, touching.Scissor);
	}

	[Fact]
	public void EffectiveTransformAppliesTheViewMatrixLast() {
		var view = Matrix3x2.CreateScale(2);
		CanvasParams p = CanvasParams.Primary(view, SrgbColor32.Black)
			.Merge(new(Transform: CanvasTransformOverride.Set(Matrix3x2.CreateTranslation(1, 0))));
		Assert.Equal(new Vector2(2, 0), Vector2.Transform(Vector2.Zero, p.EffectiveTransform));
		Assert.Throws<InvalidDefaultException>(() => default(CanvasParams).EffectiveTransform);
	}

	[Fact]
	public void MergeDoesntValidateTheResult() {
		// the product's inverse overflows, which PushParams would reject
		var tiny = Matrix3x2.CreateScale(1e-10f);
		CanvasParams p = baseParams.Merge(new(Transform: CanvasTransformOverride.Set(tiny)));
		Assert.Equal(tiny * tiny, p.Merge(new(Transform: CanvasTransformOverride.Multiply(tiny))).Transform);
	}

	[Fact]
	public void DefaultTransformOverridesAreRejected() {
		Assert.Throws<ArgumentInvalidDefaultException>(() => baseParams.Merge(new(Transform: default(CanvasTransformOverride))));
	}
}
