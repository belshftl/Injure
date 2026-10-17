// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Injure.Draw;

/// <summary>
/// Invertibility checks and inversion for <see cref="Matrix3x2"/>, using the definition of
/// "invertible" that <see cref="Draw"/> APIs require of transform matrices.
/// </summary>
/// <remarks>
/// <para>
/// Due to floating-point quirks, the definition used by Injure is stricter than the linear algebra
/// definition of a non-zero determinant: the matrix must have a non-zero finite determinant, have
/// all of its elements be finite, and have all of the elements of its inverse be finite. This
/// rejects e.g. a scale so small that its inverse overflows.
/// <see cref="Matrix3x2.Invert(Matrix3x2, out Matrix3x2)"/> alone doesn't check this: it only
/// rejects determinants smaller in magnitude than <see cref="float.Epsilon"/> (the smallest
/// denormal), and "inverts" a matrix whose determinant overflows to all zeros.
/// </para>
/// <para>
/// <see cref="Matrix3x2"/> is treated as a 3x3 affine matrix whose third column is
/// <c>[0, 0, 1]</c>, as <see cref="System.Numerics"/> does, so its determinant is
/// <c>M11 * M22 - M12 * M21</c> (see <see cref="Matrix3x2.GetDeterminant()"/>), and
/// <see cref="Matrix3x2.Invert(Matrix3x2, out Matrix3x2)"/> inverts that 3x3 matrix.
/// </para>
/// </remarks>
public static class MatrixInvert {
	/// <summary>
	/// Whether <paramref name="m"/> is invertible, as <see cref="MatrixInvert"/> defines it.
	/// </summary>
	/// <param name="m">Matrix to check.</param>
	public static bool IsInvertible(Matrix3x2 m) => TryInvert(m, out _);

	/// <summary>
	/// Inverts <paramref name="m"/> if it's invertible, as <see cref="MatrixInvert"/> defines it.
	/// </summary>
	/// <param name="m">Matrix to invert.</param>
	/// <param name="result">
	/// The inverse of <paramref name="m"/>, or the zero matrix if it isn't invertible.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="m"/> is invertible; otherwise,
	/// <see langword="false"/>.
	/// </returns>
	public static bool TryInvert(Matrix3x2 m, out Matrix3x2 result) {
		float det = m.GetDeterminant();
		if (isFinite(m) && float.IsFinite(det) && det != 0f
			&& Matrix3x2.Invert(m, out result) && isFinite(result))
			return true;
		result = default;
		return false;
	}

	/// <summary>
	/// Throws if <paramref name="m"/> isn't invertible, as <see cref="MatrixInvert"/> defines it.
	/// </summary>
	/// <param name="m">Matrix to check.</param>
	/// <param name="paramName">
	/// Name of the parameter <paramref name="m"/> came from; defaults to the expression passed as
	/// <paramref name="m"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="m"/> isn't invertible.
	/// </exception>
	public static void ThrowIfNotInvertible(
		Matrix3x2 m,
		[CallerArgumentExpression(nameof(m))] string? paramName = null
	) {
		if (!IsInvertible(m))
			throw new ArgumentOutOfRangeException(paramName, m, "matrix must be invertible");
	}

	private static bool isFinite(Matrix3x2 m) =>
		float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M21)
		&& float.IsFinite(m.M22) && float.IsFinite(m.M31) && float.IsFinite(m.M32);
}
