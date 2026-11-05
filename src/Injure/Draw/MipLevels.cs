// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure.Draw;

/// <summary>
/// Mip level count of a texture, or <see cref="FullChain"/> to automatically select a full chain's
/// worth.
/// </summary>
/// <remarks>
/// <para>
/// It's recommended to use a full mip chain unless you know what you're doing. The point of mips
/// is to prevent textures drawn at about half size or lower from aliasing under a linear filter,
/// but if you have a non-full mip chain, textures drawn smaller than the last level still alias.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Single"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(MipLevelsJsonConverter))]
public readonly struct MipLevels : IEquatable<MipLevels> {
	private readonly uint countm1; // count - 1, or uint.MaxValue for a full chain

	private MipLevels(uint countm1) {
		this.countm1 = countm1;
	}

	/// <summary>
	/// Only the base mip level. Equivalent to <see cref="Count(uint)"/> with a count of 1.
	/// </summary>
	public static readonly MipLevels Single = default;

	/// <summary>
	/// A full chain, down to 1x1, however many levels that takes for the texture's size.
	/// </summary>
	public static readonly MipLevels FullChain = new(uint.MaxValue);

	/// <summary>
	/// <paramref name="count"/> explicitly-specified levels, including the base level. Must be at most
	/// a full chain to use as the mip level count when creating a texture.
	/// </summary>
	/// <param name="count">Number of levels, including the base level.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="count"/> is 0.
	/// </exception>
	public static MipLevels Count(uint count) {
		ArgumentOutOfRangeException.ThrowIfZero(count);
		return new MipLevels(count - 1);
	}

	/// <summary>
	/// Whether this is <see cref="FullChain"/>.
	/// </summary>
	public bool IsFullChain => countm1 == uint.MaxValue;

	/// <summary>
	/// The explicitly-specified mip level count, including the base level. Never 0.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this is <see cref="FullChain"/>, i.e. <see cref="IsFullChain"/> is true.
	/// </exception>
	public uint ExplicitCount => IsFullChain
		? throw new InvalidOperationException("this MipLevels value is FullChain rather than an explicitly-specified count")
		: countm1 + 1;

	/// <summary>
	/// The number of levels a full chain for a texture of the given size has.
	/// </summary>
	/// <param name="width">Texture width in texels.</param>
	/// <param name="height">Texture height in texels.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="width"/> or <paramref name="height"/> is 0.
	/// </exception>
	public static uint FullChainLength(uint width, uint height) {
		ArgumentOutOfRangeException.ThrowIfZero(width);
		ArgumentOutOfRangeException.ThrowIfZero(height);
		return (uint)BitOperations.Log2(Math.Max(width, height)) + 1;
	}

	internal uint Resolve(uint width, uint height, string paramName) {
		uint full = FullChainLength(width, height);
		if (IsFullChain)
			return full;
		uint count = countm1 + 1;
		if (count > full)
			throw new ArgumentOutOfRangeException(paramName, $"{count} mip levels is more than a full chain for a {width}x{height} texture, which has {full}");
		return count;
	}

	public bool Equals(MipLevels other) => countm1 == other.countm1;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is MipLevels other && Equals(other);
	public override int GetHashCode() => countm1.GetHashCode();
	public static bool operator ==(MipLevels left, MipLevels right) => left.Equals(right);
	public static bool operator !=(MipLevels left, MipLevels right) => !left.Equals(right);

	public override string ToString() => IsFullChain
		? nameof(FullChain)
		: countm1 == 0
			? nameof(Single)
			: $"Count({countm1 + 1})";
}

/// <summary>
/// <see cref="JsonConverter{T}"/> for <see cref="MipLevels"/>, as either the JSON string
/// <c>"full"</c> for <see cref="MipLevels.FullChain"/>, or a positive integer JSON number for an
/// explicitly-specified count.
/// </summary>
public sealed class MipLevelsJsonConverter : JsonConverter<MipLevels> {
	/// <summary>
	/// Reads either the JSON string value <c>"full"</c>, corresponding to
	/// <see cref="MipLevels.FullChain"/>, or a positive integer JSON number, corresponding to
	/// <see cref="MipLevels.Count(uint)"/>.
	/// </summary>
	/// <exception cref="JsonException">
	/// Thrown if the value isn't the JSON string <c>"full"</c> or a positive integer.
	/// </exception>
	public override MipLevels Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("full"u8))
			return MipLevels.FullChain;
		if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt32(out uint n) && n > 0)
			return MipLevels.Count(n);
		throw new JsonException("mip levels value must be \"full\" or a positive integer");
	}

	/// <summary>
	/// Writes the JSON string value <c>"full"</c> if <see cref="MipLevels.IsFullChain"/>, or
	/// otherwise a positive integer JSON number <see cref="MipLevels.ExplicitCount"/>.
	/// </summary>
	public override void Write(Utf8JsonWriter writer, MipLevels value, JsonSerializerOptions options) {
		if (value.IsFullChain)
			writer.WriteStringValue("full");
		else
			writer.WriteNumberValue(value.ExplicitCount);
	}
}
