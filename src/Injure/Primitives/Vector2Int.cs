// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure.Primitives;

/// <summary>
/// 2D vector of <see langword="int"/>s, e.g. a pixel position or size.
/// </summary>
/// <param name="x">The X component.</param>
/// <param name="y">The Y component.</param>
/// <remarks>
/// <para>
/// Arithmetic operators do <see langword="int"/> arithmetic per component: they wrap on overflow
/// outside a <see langword="checked"/> context, and division rounds towards zero.
/// </para>
/// <para>
/// Laid out in memory as <see cref="int"/>s <see cref="X"/>, <see cref="Y"/>, in that order, with
/// no padding. The memory layout is fixed, not incidental. Safe for FFI,
/// <c>stackalloc Vector2Int[]</c>, bit/span reinterprets, etc.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Zero"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(Vector2IntJsonConverter))]
[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly struct Vector2Int(int x, int y) : IEquatable<Vector2Int>, IFormattable {
	/// <summary>
	/// The X component, at byte offset 0.
	/// </summary>
	[FieldOffset(0)] public readonly int X = x;

	/// <summary>
	/// The Y component, at byte offset 4.
	/// </summary>
	[FieldOffset(4)] public readonly int Y = y;

	/// <summary>
	/// Size of a <see cref="Vector2Int"/> in bytes. Equal to 8.
	/// </summary>
	public const int Size = 8;

	/// <summary>
	/// The vector (0, 0).
	/// </summary>
	public static readonly Vector2Int Zero = default;

	/// <summary>
	/// The vector (1, 1).
	/// </summary>
	public static readonly Vector2Int One = new(1, 1);

	/// <summary>
	/// The vector (1, 0).
	/// </summary>
	public static readonly Vector2Int UnitX = new(1, 0);

	/// <summary>
	/// The vector (0, 1).
	/// </summary>
	public static readonly Vector2Int UnitY = new(0, 1);

	/// <summary>
	/// Deconstructs this vector into its components.
	/// </summary>
	/// <param name="x">The X component.</param>
	/// <param name="y">The Y component.</param>
	public void Deconstruct(out int x, out int y) {
		x = X;
		y = Y;
	}

	/// <summary>
	/// Converts this vector to a <see cref="Vector2"/>, losing precision for components with magnitude
	/// above 2^24.
	/// </summary>
	/// <remarks>
	/// Lossy because not every integer above 2^24 is representable as a <see langword="float"/> due to
	/// the limited mantissa size.
	/// </remarks>
	public Vector2 ToVector2Lossy() => new(X, Y);

	/// <summary>
	/// Converts <paramref name="v"/> to a <see cref="Vector2Int"/>, rounding each component towards
	/// negative infinity.
	/// </summary>
	/// <param name="v">The vector to convert.</param>
	/// <exception cref="OverflowException">
	/// Thrown if a component is NaN or out of the range of <see langword="int"/> after rounding.
	/// </exception>
	public static Vector2Int Floor(Vector2 v) => new(toInt(MathF.Floor(v.X)), toInt(MathF.Floor(v.Y)));

	/// <summary>
	/// Converts <paramref name="v"/> to a <see cref="Vector2Int"/>, rounding each component towards
	/// positive infinity.
	/// </summary>
	/// <inheritdoc cref="Floor(Vector2)" path="/param"/>
	/// <inheritdoc cref="Floor(Vector2)" path="/exception"/>
	public static Vector2Int Ceiling(Vector2 v) => new(toInt(MathF.Ceiling(v.X)), toInt(MathF.Ceiling(v.Y)));

	/// <summary>
	/// Converts <paramref name="v"/> to a <see cref="Vector2Int"/>, rounding each component to the
	/// nearest integer, with midpoints rounded as given by <paramref name="mode"/>.
	/// </summary>
	/// <param name="v">The vector to convert.</param>
	/// <param name="mode">How to round midpoints between two integers.</param>
	/// <inheritdoc cref="Floor(Vector2)" path="/exception"/>
	public static Vector2Int Round(Vector2 v, MidpointRounding mode) =>
		new(toInt(MathF.Round(v.X, mode)), toInt(MathF.Round(v.Y, mode)));

	/// <summary>
	/// Converts <paramref name="v"/> to a <see cref="Vector2Int"/>, rounding each component towards
	/// zero.
	/// </summary>
	/// <inheritdoc cref="Floor(Vector2)" path="/param"/>
	/// <inheritdoc cref="Floor(Vector2)" path="/exception"/>
	public static Vector2Int Truncate(Vector2 v) => new(toInt(MathF.Truncate(v.X)), toInt(MathF.Truncate(v.Y)));

	// checked so that NaN and out-of-range values throw rather than saturating
	private static int toInt(float f) => checked((int)f);

	/// <summary>
	/// Returns a vector with the smaller of each pair of components.
	/// </summary>
	public static Vector2Int Min(Vector2Int a, Vector2Int b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));

	/// <summary>
	/// Returns a vector with the larger of each pair of components.
	/// </summary>
	public static Vector2Int Max(Vector2Int a, Vector2Int b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

	/// <summary>
	/// Returns a vector with the absolute value of each component.
	/// </summary>
	/// <exception cref="OverflowException">
	/// Thrown if a component is <see cref="int.MinValue"/>.
	/// </exception>
	public static Vector2Int Abs(Vector2Int v) => new(Math.Abs(v.X), Math.Abs(v.Y));

	/// <summary>Adds two vectors component-wise.</summary>
	public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.X + b.X, a.Y + b.Y);
	/// <summary>Subtracts two vectors component-wise.</summary>
	public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.X - b.X, a.Y - b.Y);
	/// <summary>Negates each component.</summary>
	public static Vector2Int operator -(Vector2Int v) => new(-v.X, -v.Y);
	/// <summary>Multiplies two vectors component-wise.</summary>
	public static Vector2Int operator *(Vector2Int a, Vector2Int b) => new(a.X * b.X, a.Y * b.Y);
	/// <summary>Multiplies each component by a scalar.</summary>
	public static Vector2Int operator *(Vector2Int v, int s) => new(v.X * s, v.Y * s);
	/// <summary>Multiplies each component by a scalar.</summary>
	public static Vector2Int operator *(int s, Vector2Int v) => new(v.X * s, v.Y * s);
	/// <summary>Divides two vectors component-wise, rounding towards zero.</summary>
	public static Vector2Int operator /(Vector2Int a, Vector2Int b) => new(a.X / b.X, a.Y / b.Y);
	/// <summary>Divides each component by a scalar, rounding towards zero.</summary>
	public static Vector2Int operator /(Vector2Int v, int s) => new(v.X / s, v.Y / s);

	public bool Equals(Vector2Int other) => X == other.X && Y == other.Y;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is Vector2Int other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y);
	public static bool operator ==(Vector2Int left, Vector2Int right) => left.Equals(right);
	public static bool operator !=(Vector2Int left, Vector2Int right) => !left.Equals(right);

	/// <summary>
	/// Returns a string representation of this vector, in the form <c>{ X = 1, Y = 2 }</c>.
	/// </summary>
	/// <remarks>
	/// Always formats in the invariant culture, regardless of the current culture.
	/// </remarks>
	public override string ToString() => ToString(null, null);

	/// <summary>
	/// Returns a string representation of this vector, in the form <c>{ X = 1, Y = 2 }</c>, with
	/// each component formatted with <paramref name="format"/>.
	/// </summary>
	/// <param name="format">Numeric format string to use for each component.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <remarks>
	/// Always formats in the invariant culture, regardless of the current culture and
	/// <paramref name="formatProvider"/>.
	/// </remarks>
	public string ToString(string? format, IFormatProvider? formatProvider) {
		CultureInfo inv = CultureInfo.InvariantCulture;
		return string.Format(inv, "{{ X = {0}, Y = {1} }}", X.ToString(format, inv), Y.ToString(format, inv));
	}
}

/// <summary>
/// <see cref="JsonConverter{T}"/> for <see cref="Vector2Int"/>s, as JSON objects of the form
/// <c>{ "x": 1, "y": 2 }</c>.
/// </summary>
public sealed class Vector2IntJsonConverter : JsonConverter<Vector2Int> {
	/// <summary>
	/// Reads a JSON object with the properties <c>x</c> and <c>y</c>, in any order, as JSON numbers.
	/// </summary>
	/// <exception cref="JsonException">
	/// Thrown if the value isn't an object, a property is missing or unknown, or a property's value
	/// isn't a JSON integer in the range of <see langword="int"/>.
	/// </exception>
	/// <remarks>
	/// Property names are matched case-insensitively if
	/// <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> is set, and case-sensitively
	/// otherwise.
	/// </remarks>
	public override Vector2Int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException("expected object");
		int? x = null, y = null;
		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (x is null || y is null)
					throw new JsonException("missing one or more Vector2Int fields");
				return new Vector2Int(x.Value, y.Value);
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
				throw new JsonException("was expecting PropertyName json token");
			string? name = reader.GetString();
			if (!reader.Read())
				throw new JsonException("unexpected end of json");

			if (eq(name, "x", options))
				x = read(ref reader, name);
			else if (eq(name, "y", options))
				y = read(ref reader, name);
			else
				throw new JsonException($"unexpected json property '{name}'");
		}
		throw new JsonException("unexpected end of json");
	}

	/// <summary>
	/// Writes a JSON object with the properties <c>x</c> and <c>y</c>.
	/// </summary>
	/// <remarks>
	/// The property names are always lowercase, regardless of
	/// <see cref="JsonSerializerOptions.PropertyNamingPolicy"/>.
	/// </remarks>
	public override void Write(Utf8JsonWriter writer, Vector2Int val, JsonSerializerOptions options) {
		writer.WriteStartObject();
		writer.WriteNumber("x", val.X);
		writer.WriteNumber("y", val.Y);
		writer.WriteEndObject();
	}

	private static int read(ref Utf8JsonReader reader, string? name) =>
		reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int v)
			? v
			: throw new JsonException($"json property '{name}' must be a JSON integer in the range of int");

	private static bool eq(string? got, string expected, JsonSerializerOptions options) =>
		string.Equals(got, expected, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
