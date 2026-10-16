// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Primitives;

namespace Injure.Tests.Draw;

public sealed class CanvasScissorTests {
	[Fact]
	public void EachEdgeIsRoundedToTheNearestPixel() {
		// edges 1.2, 4.8, 2.5, 3.5; halves round up
		Assert.Equal(new RectI(1, 3, 4, 1), CanvasScissor.FromTransformed(new RectF(1.2f, 2.5f, 3.6f, 1f), Matrix3x2.Identity));
	}

	[Fact]
	public void ScaleAndTranslationApply() {
		Matrix3x2 m = Matrix3x2.CreateScale(2) * Matrix3x2.CreateTranslation(1, 1);
		Assert.Equal(new RectI(1, 1, 4, 6), CanvasScissor.FromTransformed(new RectF(0, 0, 2, 3), m));
	}

	[Fact]
	public void AdjacentRectsStayAdjacent() {
		Matrix3x2 m = Matrix3x2.CreateScale(2);
		// shared edge at 1.5 pixels; rounding position and size separately would leave a gap or overlap
		RectI a = CanvasScissor.FromTransformed(new RectF(0, 0, 0.75f, 1), m);
		RectI b = CanvasScissor.FromTransformed(new RectF(0.75f, 0, 0.75f, 1), m);
		Assert.Equal(a.Right, b.X);
		Assert.Equal(new RectI(0, 0, 2, 2), a);
		Assert.Equal(new RectI(2, 0, 1, 2), b);
	}

	[Fact]
	public void FlipsReorderTheEdges() {
		Matrix3x2 m = Matrix3x2.CreateScale(-1, 1) * Matrix3x2.CreateTranslation(10, 0);
		Assert.Equal(new RectI(5, 0, 3, 1), CanvasScissor.FromTransformed(new RectF(2, 0, 3, 1), m));
	}

	[Fact]
	public void FloatNoiseFromAFullTurnIsTolerated() {
		Matrix3x2 m = Matrix3x2.CreateRotation(2 * MathF.PI);
		Assert.Equal(new RectI(1, 2, 3, 4), CanvasScissor.FromTransformed(new RectF(1, 2, 3, 4), m));
	}

	[Fact]
	public void QuarterTurnsSwapTheAxes() {
		RectF r = new(1, 2, 3, 4);
		// (x, y) -> (-y, x): x in [1, 4], y in [2, 6] -> x in [-6, -2], y in [1, 4]
		Assert.Equal(new RectI(-6, 1, 4, 3), CanvasScissor.FromTransformed(r, Matrix3x2.CreateRotation(MathF.PI / 2)));
		// (x, y) -> (y, -x)
		Assert.Equal(new RectI(2, -4, 4, 3), CanvasScissor.FromTransformed(r, Matrix3x2.CreateRotation(-MathF.PI / 2)));
		// (x, y) -> (-x, -y)
		Assert.Equal(new RectI(-4, -6, 3, 4), CanvasScissor.FromTransformed(r, Matrix3x2.CreateRotation(MathF.PI)));
		// scaled, then turned, then translated: (x, y) -> (-2y + 10, 2x)
		Matrix3x2 m = Matrix3x2.CreateScale(2) * Matrix3x2.CreateRotation(MathF.PI / 2) * Matrix3x2.CreateTranslation(10, 0);
		Assert.Equal(new RectI(-2, 2, 8, 6), CanvasScissor.FromTransformed(r, m));
	}

	[Fact]
	public void OtherRotationsAndShearsAreRejected() {
		RectF r = new(0, 0, 1, 1);
		Assert.Throws<ArgumentOutOfRangeException>(() => CanvasScissor.FromTransformed(r, Matrix3x2.CreateRotation(0.1f)));
		Assert.Throws<ArgumentOutOfRangeException>(() => CanvasScissor.FromTransformed(r, Matrix3x2.CreateRotation(MathF.PI / 4)));
		Assert.Throws<ArgumentOutOfRangeException>(() => CanvasScissor.FromTransformed(r, Matrix3x2.CreateSkew(0.1f, 0)));
	}

	[Fact]
	public void NonFiniteTransformsAreRejected() {
		Matrix3x2 m = Matrix3x2.Identity with { M31 = float.NaN };
		Assert.Throws<ArgumentOutOfRangeException>(() => CanvasScissor.FromTransformed(new RectF(0, 0, 1, 1), m));
	}

	[Fact]
	public void HugeEdgesAreClampedWithoutOverflow() {
		RectI r = CanvasScissor.FromTransformed(new RectF(-1, -1, 2, 2), Matrix3x2.CreateScale(1e10f));
		Assert.Equal(new RectI(-(1 << 24), -(1 << 24), 1 << 25, 1 << 25), r);
	}

	[Fact]
	public void NonInvertibleTransformsAreRejected() {
		Assert.Throws<ArgumentOutOfRangeException>(() => CanvasScissor.FromTransformed(new RectF(0, 0, 1, 1), Matrix3x2.CreateScale(0)));
		Assert.Throws<ArgumentOutOfRangeException>(() => CanvasScissor.FromTransformed(new RectF(0, 0, 1, 1), Matrix3x2.CreateScale(1, 0)));
	}
}
