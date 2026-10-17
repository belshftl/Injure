// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure.Primitives;

/// <summary>
/// Half-open rectangle represented by 4 <see langword="float"/> components: X, Y, width, height.
/// Width/height cannot be negative but may be zero. All four components are finite ("finite"
/// includes non-NaN).
/// </summary>
/// <remarks>
/// <para>
/// <b>The memory layout is unspecified.</b>
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Empty"/>, a rectangle at the
/// origin with zero width and height.
/// </para>
/// </remarks>
[JsonConverter(typeof(RectFJsonConverter))]
public readonly struct RectF : IEquatable<RectF>, IFormattable {
	/// <summary>
	/// X coordinate of the rectangle's left edge. Always finite.
	/// </summary>
	public readonly float X;

	/// <summary>
	/// Y coordinate of the rectangle's top edge. Always finite.
	/// </summary>
	public readonly float Y;

	/// <summary>
	/// Width of the rectangle. Always finite and non-negative; may be zero.
	/// </summary>
	public readonly float Width;

	/// <summary>
	/// Height of the rectangle. Always finite and non-negative; may be zero.
	/// </summary>
	public readonly float Height;

	/// <summary>
	/// Initializes a new rectangle from a position and size.
	/// </summary>
	/// <param name="x">Finite X coordinate of the rectangle's left edge.</param>
	/// <param name="y">Finite Y coordinate of the rectangle's top edge.</param>
	/// <param name="width">Finite non-negative width of the rectangle.</param>
	/// <param name="height">Finite non-negative height of the rectangle.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if any argument is not finite, <paramref name="width"/> or <paramref name="height"/>
	/// is negative, or the derived right/bottom edge is not finite.
	/// </exception>
	public RectF(float x, float y, float width, float height) {
		if (!float.IsFinite(x))
			throw new ArgumentOutOfRangeException(nameof(x), x, "X coordinate must be finite");
		if (!float.IsFinite(y))
			throw new ArgumentOutOfRangeException(nameof(y), y, "Y coordinate must be finite");
		if (!float.IsFinite(width) || width < 0f)
			throw new ArgumentOutOfRangeException(nameof(width), width, "width must be finite and non-negative");
		if (!float.IsFinite(height) || height < 0f)
			throw new ArgumentOutOfRangeException(nameof(height), height, "height must be finite and non-negative");
		if (!float.IsFinite(x + width))
			throw new ArgumentOutOfRangeException(nameof(width), width, "derived right edge must be finite");
		if (!float.IsFinite(y + height))
			throw new ArgumentOutOfRangeException(nameof(height), height, "derived bottom edge must be finite");

		X = x;
		Y = y;
		Width = width;
		Height = height;
	}

	/// <summary>
	/// A rectangle at the origin with zero width and zero height.
	/// </summary>
	public static readonly RectF Empty = default;

	/// <summary>
	/// Inclusive left edge of the rectangle. Alias of <see cref="X"/>.
	/// </summary>
	public float Left => X;

	/// <summary>
	/// Inclusive top edge of the rectangle. Alias of <see cref="Y"/>.
	/// </summary>
	public float Top => Y;

	/// <summary>
	/// Exclusive right edge of the rectangle.
	/// </summary>
	public float Right => X + Width;

	/// <summary>
	/// Exclusive bottom edge of the rectangle.
	/// </summary>
	public float Bottom => Y + Height;

	/// <summary>
	/// The rectangle's position.
	/// </summary>
	public Vector2 Position => new(X, Y);

	/// <summary>
	/// The rectangle's size.
	/// </summary>
	public Vector2 Size => new(Width, Height);

	/// <summary>
	/// X coordinate of the rectangle's center.
	/// </summary>
	public float CenterX => X + Width * 0.5f;

	/// <summary>
	/// Y coordinate of the rectangle's center.
	/// </summary>
	public float CenterY => Y + Height * 0.5f;

	/// <summary>
	/// The rectangle's center point.
	/// </summary>
	public Vector2 Center => new(CenterX, CenterY);

	/// <summary>
	/// Whether the rectangle's dimensions are zero.
	/// </summary>
	public bool IsEmpty => Width == 0f || Height == 0f;

	/// <summary>
	/// Whether the rectangle has an area (neither dimensions are zero).
	/// </summary>
	public bool HasArea => Width > 0f && Height > 0f;

	/// <summary>
	/// The rectangle's area.
	/// </summary>
	public float Area => Width * Height;

	/// <summary>
	/// Creates a rectangle from left, top, right, and bottom edges.
	/// </summary>
	/// <param name="left">The inclusive left edge.</param>
	/// <param name="top">The inclusive top edge.</param>
	/// <param name="right">
	/// The exclusive right edge. Must be greater than or equal to <paramref name="left"/>.
	/// </param>
	/// <param name="bottom">
	/// The exclusive bottom edge. Must be greater than or equal to <paramref name="top"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown when any edge is not finite, when <paramref name="right"/> is less than
	/// <paramref name="left"/>, when <paramref name="bottom"/> is less than
	/// <paramref name="top"/>, or when the derived size is not finite.
	/// </exception>
	public static RectF FromLtrb(float left, float top, float right, float bottom) {
		if (!float.IsFinite(left))
			throw new ArgumentOutOfRangeException(nameof(left), left, "left edge must be finite");
		if (!float.IsFinite(top))
			throw new ArgumentOutOfRangeException(nameof(top), top, "top edge must be finite");
		if (!float.IsFinite(right))
			throw new ArgumentOutOfRangeException(nameof(right), right, "right edge must be finite");
		if (!float.IsFinite(bottom))
			throw new ArgumentOutOfRangeException(nameof(bottom), bottom, "bottom edge must be finite");
		if (right < left)
			throw new ArgumentOutOfRangeException(nameof(right), right, "right edge must be greater than or equal to the left edge");
		if (bottom < top)
			throw new ArgumentOutOfRangeException(nameof(bottom), bottom, "bottom edge must be greater than or equal to the top edge");
		return new RectF(left, top, right - left, bottom - top);
	}

	/// <summary>
	/// Returns whether the rectangle contains the specified point.
	/// </summary>
	/// <param name="point">The point to test.</param>
	public bool Contains(Vector2 point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

	/// <summary>
	/// Returns whether the rectangle contains the specified point.
	/// </summary>
	/// <param name="x">X coordinate of the point to test.</param>
	/// <param name="y">Y coordinate of the point to test.</param>
	public bool Contains(float x, float y) => x >= Left && x < Right && y >= Top && y < Bottom;

	/// <summary>
	/// Returns whether this rectangle fully contains another rectangle.
	/// </summary>
	/// <param name="other">The rectangle to test.</param>
	public bool Contains(RectF other) => other.HasArea ? other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom : Contains(other.Position);

	/// <summary>
	/// Attempts to compute the intersection of this rectangle and another rectangle.
	/// </summary>
	/// <param name="other">The rectangle to intersect with this rectangle.</param>
	/// <param name="result">
	/// The intersection rectangle, or <see cref="Empty"/> if the rectangles do not intersect.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if the rectangles overlap with positive area; otherwise,
	/// <see langword="false"/>.
	/// </returns>
	public bool TryIntersect(RectF other, out RectF result) {
		float left = MathF.Max(Left, other.Left);
		float top = MathF.Max(Top, other.Top);
		float right = MathF.Min(Right, other.Right);
		float bottom = MathF.Min(Bottom, other.Bottom);
		if (right <= left || bottom <= top) {
			result = Empty;
			return false;
		}
		result = FromLtrb(left, top, right, bottom);
		return true;
	}

	/// <summary>
	/// Returns the bounding rectangle containing this rectangle and another rectangle.
	/// </summary>
	/// <param name="other">The rectangle to include.</param>
	/// <remarks>
	/// Zero-area rectangles do not enlarge the result. If both rectangles are zero-area,
	/// <see cref="Empty"/> is returned.
	/// </remarks>
	public RectF Union(RectF other) {
		if (!HasArea)
			return other.HasArea ? other : Empty;
		if (!other.HasArea)
			return this;
		return FromLtrb(MathF.Min(Left, other.Left), MathF.Min(Top, other.Top), MathF.Max(Right, other.Right), MathF.Max(Bottom, other.Bottom));
	}

	/// <summary>
	/// Returns the bounding rectangle containing this rectangle's and another rectangle's
	/// edges, including zero-area rectangles.
	/// </summary>
	/// <param name="other">The rectangle to include.</param>
	public RectF UnionExtents(RectF other) => FromLtrb(MathF.Min(Left, other.Left), MathF.Min(Top, other.Top), MathF.Max(Right, other.Right), MathF.Max(Bottom, other.Bottom));

	/// <summary>
	/// Returns whether this rectangle intersects another rectangle.
	/// </summary>
	/// <param name="other">The rectangle to test.</param>
	public bool Intersects(RectF other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

	/// <summary>
	/// Returns whether this rectangle is approximately equal to another rectangle.
	/// </summary>
	/// <param name="other">The rectangle to compare with this rectangle.</param>
	/// <param name="epsilon">The maximum absolute per-component difference.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="epsilon"/> is not finite or is negative.
	/// </exception>
	public bool NearlyEquals(RectF other, float epsilon) {
		if (!float.IsFinite(epsilon) || epsilon < 0f)
			throw new ArgumentOutOfRangeException(nameof(epsilon), epsilon, "epsilon value must be finite and non-negative");
		return MathF.Abs(X - other.X) <= epsilon && MathF.Abs(Y - other.Y) <= epsilon &&
			MathF.Abs(Width - other.Width) <= epsilon && MathF.Abs(Height - other.Height) <= epsilon;
	}

	/// <summary>
	/// Returns whether this rectangle is exactly equal to another rectangle.
	/// </summary>
	/// <param name="other">The rectangle to compare with this rectangle.</param>
	public bool Equals(RectF other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is RectF other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
	public static bool operator ==(RectF left, RectF right) => left.Equals(right);
	public static bool operator !=(RectF left, RectF right) => !left.Equals(right);

	/// <summary>
	/// Returns a string representation of this rectangle, in the form
	/// <c>{ X = 1, Y = 2, Width = 3, Height = 4 }</c>.
	/// </summary>
	/// <remarks>
	/// Always formats in the invariant culture, regardless of the current culture.
	/// </remarks>
	public override string ToString() => ToString(null, null);

	/// <summary>
	/// Returns a string representation of this rectangle, in the form
	/// <c>{ X = 1, Y = 2, Width = 3, Height = 4 }</c>, with each component formatted with
	/// <paramref name="format"/>.
	/// </summary>
	/// <param name="format">Numeric format string to use for each component.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <remarks>
	/// Always formats in the invariant culture, regardless of the current culture and
	/// <paramref name="formatProvider"/>.
	/// </remarks>
	public string ToString(string? format, IFormatProvider? formatProvider) {
		CultureInfo inv = CultureInfo.InvariantCulture;
		return string.Format(
			inv,
			"{{ X = {0}, Y = {1}, Width = {2}, Height = {3} }}",
			X.ToString(format, inv),
			Y.ToString(format, inv),
			Width.ToString(format, inv),
			Height.ToString(format, inv)
		);
	}
}

/// <summary>
/// <see cref="JsonConverter{T}"/> for <see cref="RectF"/>s, as JSON objects of the form
/// <c>{ "x": 1, "y": 2, "width": 3, "height": 4 }</c>.
/// </summary>
public sealed class RectFJsonConverter : JsonConverter<RectF> {
	/// <summary>
	/// Reads a JSON object with the properties <c>x</c>, <c>y</c>, <c>width</c>, and <c>height</c>,
	/// in any order, as JSON numbers.
	/// </summary>
	/// <exception cref="JsonException">
	/// Thrown if the value isn't an object, a property is missing or unknown, a property's value isn't
	/// a JSON number representable as a finite <see langword="float"/>, or the values don't form a
	/// valid <see cref="RectF"/> (see <see cref="RectF(float, float, float, float)"/>).
	/// </exception>
	/// <remarks>
	/// Property names are matched case-insensitively if
	/// <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> is set, and case-sensitively
	/// otherwise.
	/// </remarks>
	public override RectF Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException("expected object");
		float? x = null, y = null, width = null, height = null;
		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (x is null || y is null || width is null || height is null)
					throw new JsonException("missing one or more RectF fields");
				try {
					return new RectF(x.Value, y.Value, width.Value, height.Value);
				} catch (ArgumentOutOfRangeException ex) {
					throw new JsonException($"invalid RectF: {ex.Message}", ex);
				}
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
			else if (eq(name, "width", options))
				width = read(ref reader, name);
			else if (eq(name, "height", options))
				height = read(ref reader, name);
			else
				throw new JsonException($"unexpected json property '{name}'");
		}
		throw new JsonException("unexpected end of json");
	}

	/// <summary>
	/// Writes a JSON object with the properties <c>x</c>, <c>y</c>, <c>width</c>, and <c>height</c>.
	/// </summary>
	/// <remarks>
	/// The property names are always lowercase, regardless of
	/// <see cref="JsonSerializerOptions.PropertyNamingPolicy"/>.
	/// </remarks>
	public override void Write(Utf8JsonWriter writer, RectF val, JsonSerializerOptions options) {
		writer.WriteStartObject();
		writer.WriteNumber("x", val.X);
		writer.WriteNumber("y", val.Y);
		writer.WriteNumber("width", val.Width);
		writer.WriteNumber("height", val.Height);
		writer.WriteEndObject();
	}

	private static float read(ref Utf8JsonReader reader, string? name) =>
		reader.TokenType == JsonTokenType.Number && reader.TryGetSingle(out float v)
			? v
			: throw new JsonException($"json property '{name}' must be a number representable as a finite float");

	private static bool eq(string? got, string expected, JsonSerializerOptions options) =>
		string.Equals(got, expected, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
