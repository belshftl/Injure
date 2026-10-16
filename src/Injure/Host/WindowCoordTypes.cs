// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace Injure.Host;

/// <summary>
/// A position in a window's coordinates.
/// </summary>
/// <param name="x">Horizontal position, in window coordinates.</param>
/// <param name="y">Vertical position, in window coordinates.</param>
/// <remarks>
/// <para>
/// Window coordinates are what the OS reports pointer positions in. The origin is the window's top
/// left, and +Y points down. They don't map 1:1 to draw coordinates: one window coordinate spans
/// <c>d</c> physical pixels where <c>d</c> is the window's pixel density, and draw coordinates
/// usually go through a view matrix (e.g. scaling by the pixel density) and possibly a letterboxed
/// virtual resolution.
/// </para>
/// <para>
/// Use <see cref="Draw.WindowCoords"/> if you want to map a pointer position to target pixels or
/// draw coordinates; <see cref="ToRaw()"/> should only be used if you know what you're doing.
/// </para>
/// <para>
/// A position and a movement map differently (a movement mustn't be translated), so movements are
/// a separate type, <see cref="WindowDelta"/>. Subtracting two positions gives a
/// <see cref="WindowDelta"/>, and adding one to a position gives a position.
/// </para>
/// <para>
/// Components aren't validated to be finite, since they come as-is from the OS or another event
/// source.
/// </para>
/// <para>
/// <b>The memory layout is unspecified.</b>
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Zero"/>.
/// </para>
/// </remarks>
public readonly struct WindowPos(float x, float y) : IEquatable<WindowPos>, IFormattable {
	/// <summary>
	/// Horizontal position, in window coordinates.
	/// </summary>
	public readonly float X = x;

	/// <summary>
	/// Vertical position, in window coordinates. +Y is down, not up.
	/// </summary>
	public readonly float Y = y;

	/// <summary>
	/// The window's origin, i.e. (0, 0).
	/// </summary>
	public static readonly WindowPos Zero = default;

	/// <summary>
	/// Creates a position from the components of <paramref name="raw"/>, taken as window
	/// coordinates.
	/// </summary>
	/// <param name="raw">Position in window coordinates.</param>
	public static WindowPos FromRaw(Vector2 raw) => new(raw.X, raw.Y);

	/// <summary>
	/// Returns the components as a <see cref="Vector2"/>, still in window coordinates.
	/// </summary>
	/// <remarks>
	/// <b>This is usually not what you want.</b> Window coordinates don't map 1:1 to draw
	/// coordinates, so the result will be wrong if interpreted as a draw coordinate; it may still
	/// happen to coincide depending on the setup, especially on non-HiDPI displays, but it's
	/// incorrect. Use <see cref="Draw.WindowCoords.ToDraw(WindowPos, float, Matrix3x2)"/> to map to
	/// draw coordinates, or <see cref="Draw.WindowCoords.ToPixels(WindowPos, float)"/> to map to
	/// target pixels.
	/// </remarks>
	public Vector2 ToRaw() => new(X, Y);

	/// <summary>Moves a position by a movement.</summary>
	public static WindowPos operator +(WindowPos p, WindowDelta d) => new(p.X + d.X, p.Y + d.Y);
	/// <summary>Moves a position back by a movement.</summary>
	public static WindowPos operator -(WindowPos p, WindowDelta d) => new(p.X - d.X, p.Y - d.Y);
	/// <summary>The movement from <paramref name="from"/> to <paramref name="to"/>.</summary>
	public static WindowDelta operator -(WindowPos to, WindowPos from) => new(to.X - from.X, to.Y - from.Y);

	public bool Equals(WindowPos other) => X.Equals(other.X) && Y.Equals(other.Y);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is WindowPos other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y);
	public static bool operator ==(WindowPos left, WindowPos right) => left.Equals(right);
	public static bool operator !=(WindowPos left, WindowPos right) => !left.Equals(right);

	/// <summary>
	/// Returns a string representation of this position, in the form <c>{ X = 1, Y = 2 }</c>.
	/// </summary>
	/// <remarks>
	/// Always formats in the invariant culture, regardless of the current culture.
	/// </remarks>
	public override string ToString() => ToString(null, null);

	/// <summary>
	/// Returns a string representation of this position, in the form <c>{ X = 1, Y = 2 }</c>, with
	/// each component formatted with <paramref name="format"/>.
	/// </summary>
	/// <param name="format">Numeric format string to use for each component.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <remarks>
	/// Always formats in the invariant culture, regardless of the current culture and
	/// <paramref name="formatProvider"/>.
	/// </remarks>
	public string ToString(string? format, IFormatProvider? formatProvider) =>
		WindowCoordFormat.Format(X, Y, format);
}

/// <summary>
/// A change in position ("movement") in a window's coordinates.
/// </summary>
/// <param name="x">Horizontal movement, in window coordinates.</param>
/// <param name="y">Vertical movement, in window coordinates.</param>
/// <remarks>
/// <para>
/// See <see cref="WindowPos"/> for what window coordinates are. Is a distinct type because, unlike
/// a position, a movement maps to draw coordinates without a translation (if any, e.g. from a view
/// matrix).
/// </para>
/// <para>
/// Use <see cref="Draw.WindowCoords"/> if you want to map a pointer movement to target pixels or
/// draw coordinates; <see cref="ToRaw()"/> should only be used if you know what you're doing.
/// </para>
/// <para>
/// Components aren't validated to be finite, since they come as-is from the OS or another event
/// source.
/// </para>
/// <para>
/// <b>The memory layout is unspecified.</b>
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Zero"/>.
/// </para>
/// </remarks>
public readonly struct WindowDelta(float x, float y) : IEquatable<WindowDelta>, IFormattable {
	/// <summary>
	/// Horizontal movement, in window coordinates.
	/// </summary>
	public readonly float X = x;

	/// <summary>
	/// Vertical movement, in window coordinates; +Y is down.
	/// </summary>
	public readonly float Y = y;

	/// <summary>
	/// No movement, i.e. the delta (0, 0).
	/// </summary>
	public static readonly WindowDelta Zero = default;

	/// <summary>
	/// Creates a movement from the components of <paramref name="raw"/>, taken as window
	/// coordinates.
	/// </summary>
	/// <param name="raw">Movement in window coordinates.</param>
	public static WindowDelta FromRaw(Vector2 raw) => new(raw.X, raw.Y);

	/// <summary>
	/// Returns the components as a <see cref="Vector2"/>, still in window coordinates.
	/// </summary>
	/// <remarks>
	/// <b>This is usually not what you want.</b> Window coordinates don't map 1:1 to draw
	/// coordinates, so the result will be wrong if interpreted as a movement in draw coordinates; it
	/// may still happen to coincide depending on the setup, especially on non-HiDPI displays, but
	/// it's incorrect. Use <see cref="Draw.WindowCoords.ToDraw(WindowDelta, float, Matrix3x2)"/> to
	/// map to draw coordinates, or <see cref="Draw.WindowCoords.ToPixels(WindowDelta, float)"/> to
	/// map to target pixels.
	/// </remarks>
	public Vector2 ToRaw() => new(X, Y);

	/// <summary>Adds two movements.</summary>
	public static WindowDelta operator +(WindowDelta a, WindowDelta b) => new(a.X + b.X, a.Y + b.Y);
	/// <summary>Subtracts two movements.</summary>
	public static WindowDelta operator -(WindowDelta a, WindowDelta b) => new(a.X - b.X, a.Y - b.Y);
	/// <summary>Reverses a movement.</summary>
	public static WindowDelta operator -(WindowDelta d) => new(-d.X, -d.Y);
	/// <summary>Scales a movement.</summary>
	public static WindowDelta operator *(WindowDelta d, float s) => new(d.X * s, d.Y * s);
	/// <summary>Scales a movement.</summary>
	public static WindowDelta operator *(float s, WindowDelta d) => new(d.X * s, d.Y * s);
	/// <summary>Scales a movement by the reciprocal of <paramref name="s"/>.</summary>
	public static WindowDelta operator /(WindowDelta d, float s) => new(d.X / s, d.Y / s);

	public bool Equals(WindowDelta other) => X.Equals(other.X) && Y.Equals(other.Y);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is WindowDelta other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y);
	public static bool operator ==(WindowDelta left, WindowDelta right) => left.Equals(right);
	public static bool operator !=(WindowDelta left, WindowDelta right) => !left.Equals(right);

	/// <summary>
	/// Returns a string representation of this movement, in the form <c>{ X = 1, Y = 2 }</c>.
	/// </summary>
	/// <remarks>
	/// Always formats in the invariant culture, regardless of the current culture.
	/// </remarks>
	public override string ToString() => ToString(null, null);

	/// <summary>
	/// Returns a string representation of this movement, in the form <c>{ X = 1, Y = 2 }</c>, with
	/// each component formatted with <paramref name="format"/>.
	/// </summary>
	/// <param name="format">Numeric format string to use for each component.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <remarks>
	/// Always formats in the invariant culture, regardless of the current culture and
	/// <paramref name="formatProvider"/>.
	/// </remarks>
	public string ToString(string? format, IFormatProvider? formatProvider) =>
		WindowCoordFormat.Format(X, Y, format);
}

internal static class WindowCoordFormat {
	public static string Format(float x, float y, string? format) {
		CultureInfo inv = CultureInfo.InvariantCulture;
		return string.Format(inv, "{{ X = {0}, Y = {1} }}", x.ToString(format, inv), y.ToString(format, inv));
	}
}
