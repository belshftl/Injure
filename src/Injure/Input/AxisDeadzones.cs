// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Input;

/// <summary>
/// The kind of an <see cref="AxisDeadzone"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct AxisDeadzoneKind {
	/// <summary>Raw switch tag for <see cref="AxisDeadzoneKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// No deadzone; values pass through unchanged.
		/// </summary>
		None,

		/// <summary>
		/// Values with a magnitude of at most <see cref="AxisDeadzone.Inner"/> become 0, and other values
		/// are clamped to [-<see cref="AxisDeadzone.Outer"/>, <see cref="AxisDeadzone.Outer"/>].
		/// </summary>
		Threshold,

		/// <summary>
		/// Magnitudes from <see cref="AxisDeadzone.Inner"/> to <see cref="AxisDeadzone.Outer"/> are
		/// rescaled to [0, 1], so the output starts at 0 right outside the deadzone and reaches 1 at
		/// <see cref="AxisDeadzone.Outer"/>.
		/// </summary>
		Scaled,
	}
}

/// <summary>
/// A deadzone for a 1D axis.
/// </summary>
/// <remarks>
/// <para>
/// Only constructible through the factories, which require
/// 0 &lt;= <see cref="Inner"/> &lt; <see cref="Outer"/> &lt;= 1.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is no deadzone, same as <see cref="None"/>.
/// </para>
/// </remarks>
public readonly record struct AxisDeadzone {
	/// <summary>The kind of deadzone.</summary>
	public AxisDeadzoneKind Kind { get; }

	/// <summary>
	/// Magnitude at or below which values become 0.
	/// </summary>
	public float Inner { get; }

	/// <summary>
	/// Magnitude that values are clamped to, or that rescaled values reach 1 at.
	/// </summary>
	public float Outer { get; }

	private AxisDeadzone(AxisDeadzoneKind kind, float inner, float outer) {
		DeadzoneBounds.Validate(inner, outer);
		Kind = kind;
		Inner = inner;
		Outer = outer;
	}

	/// <summary>
	/// No deadzone.
	/// </summary>
	public static readonly AxisDeadzone None = default;

	/// <summary>
	/// Creates an <see cref="AxisDeadzoneKind.Threshold"/> deadzone.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="inner"/> is not in [0, 1), or <paramref name="outer"/> is not in
	/// (<paramref name="inner"/>, 1].
	/// </exception>
	public static AxisDeadzone Threshold(float inner, float outer = 1f) =>
		new(AxisDeadzoneKind.Threshold, inner, outer);

	/// <summary>
	/// Creates an <see cref="AxisDeadzoneKind.Scaled"/> deadzone.
	/// </summary>
	/// <inheritdoc cref="Threshold" path="/exception"/>
	public static AxisDeadzone Scaled(float inner, float outer = 1f) =>
		new(AxisDeadzoneKind.Scaled, inner, outer);

	/// <summary>
	/// Applies this deadzone to <paramref name="v"/>.
	/// </summary>
	public float Apply(float v) {
		if (Kind == AxisDeadzoneKind.None)
			return v;

		float av = MathF.Abs(v);
		if (av <= Inner)
			return 0f;

		if (Kind == AxisDeadzoneKind.Threshold)
			return Math.Clamp(v, -Outer, Outer);
		if (Outer <= Inner)
			throw new InternalStateException("the factories should've guaranteed Outer > Inner");
		float mag = (av - Inner) / (Outer - Inner);
		mag = Math.Clamp(mag, 0f, 1f);
		return v < 0f ? -mag : mag;
	}
}

/// <summary>
/// The kind of an <see cref="Axis2dDeadzone"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct Axis2dDeadzoneKind {
	/// <summary>Raw switch tag for <see cref="Axis2dDeadzoneKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// No deadzone; values pass through unchanged.
		/// </summary>
		None,

		/// <summary>
		/// Vectors with a magnitude of at most <see cref="Axis2dDeadzone.Inner"/> become 0; others
		/// keep their direction and have their magnitude clamped to <see cref="Axis2dDeadzone.Outer"/>.
		/// </summary>
		Radial,

		/// <summary>
		/// Like <see cref="Radial"/>, but magnitudes from <see cref="Axis2dDeadzone.Inner"/> to
		/// <see cref="Axis2dDeadzone.Outer"/> are rescaled to [0, 1].
		/// </summary>
		ScaledRadial,

		/// <summary>
		/// Each component is treated like an <see cref="AxisDeadzoneKind.Threshold"/> 1D deadzone on
		/// its own, which snaps near-axis input onto the axis.
		/// </summary>
		Axial,

		/// <summary>
		/// Each component is treated like an <see cref="AxisDeadzoneKind.Scaled"/> 1D deadzone on its
		/// own.
		/// </summary>
		ScaledAxial,
	}
}

/// <summary>
/// A deadzone for a 2D axis.
/// </summary>
/// <remarks>
/// <para>
/// Only constructible through the factories, which require
/// 0 &lt;= <see cref="Inner"/> &lt; <see cref="Outer"/> &lt;= 1.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is no deadzone, same as <see cref="None"/>.
/// </para>
/// </remarks>
public readonly record struct Axis2dDeadzone {
	/// <summary>The kind of deadzone.</summary>
	public Axis2dDeadzoneKind Kind { get; }

	/// <summary>
	/// Magnitude at or below which values (or components, for axial kinds) become 0.
	/// </summary>
	public float Inner { get; }

	/// <summary>
	/// Magnitude that values are clamped to, or that rescaled values reach 1 at.
	/// </summary>
	public float Outer { get; }

	private Axis2dDeadzone(Axis2dDeadzoneKind kind, float inner, float outer) {
		DeadzoneBounds.Validate(inner, outer);
		Kind = kind;
		Inner = inner;
		Outer = outer;
	}

	/// <summary>
	/// No deadzone.
	/// </summary>
	public static readonly Axis2dDeadzone None = default;

	/// <summary>
	/// Creates an <see cref="Axis2dDeadzoneKind.Radial"/> deadzone.
	/// </summary>
	/// <inheritdoc cref="AxisDeadzone.Threshold" path="/exception"/>
	public static Axis2dDeadzone Radial(float inner, float outer = 1f) =>
		new(Axis2dDeadzoneKind.Radial, inner, outer);

	/// <summary>
	/// Creates an <see cref="Axis2dDeadzoneKind.ScaledRadial"/> deadzone.
	/// </summary>
	/// <inheritdoc cref="AxisDeadzone.Threshold" path="/exception"/>
	public static Axis2dDeadzone ScaledRadial(float inner, float outer = 1f) =>
		new(Axis2dDeadzoneKind.ScaledRadial, inner, outer);

	/// <summary>
	/// Creates an <see cref="Axis2dDeadzoneKind.Axial"/> deadzone.
	/// </summary>
	/// <inheritdoc cref="AxisDeadzone.Threshold" path="/exception"/>
	public static Axis2dDeadzone Axial(float inner, float outer = 1f) =>
		new(Axis2dDeadzoneKind.Axial, inner, outer);

	/// <summary>
	/// Creates an <see cref="Axis2dDeadzoneKind.ScaledAxial"/> deadzone.
	/// </summary>
	/// <inheritdoc cref="AxisDeadzone.Threshold" path="/exception"/>
	public static Axis2dDeadzone ScaledAxial(float inner, float outer = 1f) =>
		new(Axis2dDeadzoneKind.ScaledAxial, inner, outer);

	/// <summary>
	/// Applies this deadzone to <paramref name="v"/>.
	/// </summary>
	public Vector2 Apply(Vector2 v) {
		if (Kind == Axis2dDeadzoneKind.None)
			return v;
		if (Outer <= Inner)
			throw new InternalStateException("the factories should've guaranteed Outer > Inner");
		return Kind.Tag switch {
			Axis2dDeadzoneKind.Case.Radial => applyRadial(v, scaled: false),
			Axis2dDeadzoneKind.Case.ScaledRadial => applyRadial(v, scaled: true),
			Axis2dDeadzoneKind.Case.Axial => new Vector2(applyAxis(v.X, scaled: false), applyAxis(v.Y, scaled: false)),
			Axis2dDeadzoneKind.Case.ScaledAxial => new Vector2(applyAxis(v.X, scaled: true), applyAxis(v.Y, scaled: true)),
			_ => throw new UnreachableException(),
		};
	}

	private Vector2 applyRadial(Vector2 v, bool scaled) {
		float len = v.Length();
		if (len <= Inner)
			return Vector2.Zero;

		Vector2 dir = v / len;
		if (!scaled)
			return clampMagnitude(v, Outer);
		float mag = (len - Inner) / (Outer - Inner);
		mag = Math.Clamp(mag, 0f, 1f);
		return dir * mag;
	}

	private float applyAxis(float x, bool scaled) {
		float ax = MathF.Abs(x);
		if (ax <= Inner)
			return 0f;

		if (!scaled)
			return Math.Clamp(x, -Outer, Outer);
		float mag = (ax - Inner) / (Outer - Inner);
		mag = Math.Clamp(mag, 0f, 1f);
		return x < 0f ? -mag : mag;
	}

	internal static Vector2 ClampMagnitude1(Vector2 v) => clampMagnitude(v, 1.0f);

	private static Vector2 clampMagnitude(Vector2 v, float max) {
		float lenSq = v.LengthSquared();
		if (lenSq <= max * max)
			return v;
		if (lenSq <= 0f)
			return Vector2.Zero;
		return v * (max / MathF.Sqrt(lenSq));
	}
}

internal static class DeadzoneBounds {
	public static void Validate(float inner, float outer) {
		// written so that NaN fails every check
		if (!(inner >= 0f && inner < 1f))
			throw new ArgumentOutOfRangeException(nameof(inner), inner, "inner deadzone bound must be in [0, 1)");
		if (!(outer > inner && outer <= 1f))
			throw new ArgumentOutOfRangeException(nameof(outer), outer, "outer deadzone bound must be in (inner, 1]");
	}
}
