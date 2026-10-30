// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Draw;

/// <summary>
/// Describes how a <see cref="CanvasTransformOverride"/> is applied to the current transform.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct CanvasTransformOverrideKind {
	/// <summary>Raw switch tag for <see cref="CanvasTransformOverrideKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// Replaces the current transform with the given matrix.
		/// </summary>
		Set = 1,

		/// <summary>
		/// Applies the given matrix in the space of the current transform, i.e. the new transform is
		/// <c>matrix * current</c>: positions are transformed by the given matrix first, then by the
		/// current transform.
		/// </summary>
		Multiply,
	}
}

/// <summary>
/// A change to the current transform, for <see cref="CanvasParamsOverride.Transform"/>.
/// </summary>
/// <remarks>
/// <para>
/// Matrices follow the <see cref="System.Numerics"/> convention of row vectors, so
/// <c>a * b</c> applies <c>a</c> first, then <c>b</c>. <see cref="Multiply(Matrix3x2)"/> makes the
/// new transform <c>matrix * current</c>, which nests the way a scene graph does: e.g. pushing a
/// camera transform, then multiplying in an object's transform, places the object in the camera's
/// space.
/// </para>
/// <para>
/// The matrix must be invertible (see <see cref="MatrixInvert"/>), and so must the resulting
/// transform; <see cref="Canvas.PushParams(in CanvasParamsOverride)"/> checks the latter.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasTransformOverride : IEquatable<CanvasTransformOverride>, IHasInvalidDefault {
	private readonly CanvasTransformOverrideKind kind;
	private readonly Matrix3x2 matrix;

	/// <summary>
	/// How <see cref="Matrix"/> is applied to the current transform.
	/// </summary>
	public CanvasTransformOverrideKind Kind => IsDefault ? throw InvalidDefaultException.For<CanvasTransformOverride>() : kind;

	/// <summary>
	/// Matrix to set or multiply in. Always invertible.
	/// </summary>
	public Matrix3x2 Matrix => IsDefault ? throw InvalidDefaultException.For<CanvasTransformOverride>() : matrix;

	private CanvasTransformOverride(CanvasTransformOverrideKind kind, Matrix3x2 matrix) {
		this.kind = kind;
		this.matrix = matrix;
	}

	/// <summary>
	/// Creates an override that replaces the current transform with <paramref name="matrix"/>.
	/// </summary>
	/// <param name="matrix">
	/// The new transform; must be invertible (see <see cref="MatrixInvert"/>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="matrix"/> isn't invertible.
	/// </exception>
	public static CanvasTransformOverride Set(Matrix3x2 matrix) {
		MatrixInvert.ThrowIfNotInvertible(matrix);
		return new CanvasTransformOverride(CanvasTransformOverrideKind.Set, matrix);
	}

	/// <summary>
	/// Creates an override that applies <paramref name="matrix"/> in the space of the current
	/// transform, making the new transform <c>matrix * current</c>.
	/// </summary>
	/// <param name="matrix">
	/// Matrix applied to positions before the current transform; must be
	/// invertible (see <see cref="MatrixInvert"/>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="matrix"/> isn't invertible.
	/// </exception>
	public static CanvasTransformOverride Multiply(Matrix3x2 matrix) {
		MatrixInvert.ThrowIfNotInvertible(matrix);
		return new CanvasTransformOverride(CanvasTransformOverrideKind.Multiply, matrix);
	}

	public bool Equals(CanvasTransformOverride other) => kind == other.kind && matrix.Equals(other.matrix);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasTransformOverride other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(kind.RawTag, matrix);
	public static bool operator ==(CanvasTransformOverride left, CanvasTransformOverride right) => left.Equals(right);
	public static bool operator !=(CanvasTransformOverride left, CanvasTransformOverride right) => !left.Equals(right);

	internal bool IsDefault => kind == default;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
