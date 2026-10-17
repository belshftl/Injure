// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;

namespace Injure.Tests.Draw;

public static class MatrixInvertTests {
	[Fact]
	public static void InvertibleMatricesInvert() {
		Matrix3x2 m = Matrix3x2.CreateScale(2) * Matrix3x2.CreateTranslation(3, 4);
		Assert.True(MatrixInvert.TryInvert(m, out Matrix3x2 inv));
		Assert.Equal(new Vector2(1, 1), Vector2.Transform(new Vector2(5, 6), inv));
		Assert.True(MatrixInvert.IsInvertible(m));
	}

	[Fact]
	public static void StricterThanMatrix3x2Invert() {
		// the determinant (1e40) overflows; Matrix3x2.Invert "succeeds" with all zeros
		var huge = Matrix3x2.CreateScale(1e20f);
		Assert.True(Matrix3x2.Invert(huge, out _));
		Assert.False(MatrixInvert.TryInvert(huge, out Matrix3x2 inv));
		Assert.Equal(default, inv);
		// the determinant (1e-40) is a denormal above float.Epsilon, but its reciprocal overflows
		Assert.False(MatrixInvert.IsInvertible(Matrix3x2.CreateScale(1e-20f)));
		Assert.False(MatrixInvert.IsInvertible(Matrix3x2.CreateTranslation(float.NaN, 0)));
		Assert.False(MatrixInvert.IsInvertible(Matrix3x2.CreateScale(1, 0)));
	}

	[Fact]
	public static void ThrowIfNotInvertibleWorks() {
		var singular = Matrix3x2.CreateScale(0);
		ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => MatrixInvert.ThrowIfNotInvertible(singular));
		Assert.Equal("singular", ex.ParamName);
		MatrixInvert.ThrowIfNotInvertible(Matrix3x2.Identity);
	}
}
