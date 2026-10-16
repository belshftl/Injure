// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Primitives;

/// <summary>
/// 32bpp RGBA color with sRGB-encoded <see cref="R"/>/<see cref="G"/>/<see cref="B"/> and linear
/// <see cref="A"/>. This is what you want for most colors, including ones from color pickers and
/// hex codes like <c>#CBA6F7</c>.
/// </summary>
[Color32Type]
[JsonConverter(typeof(SrgbColor32JsonConverter))]
public readonly partial struct SrgbColor32 {
	/// <summary>
	/// Drops the color space information, without changing the bytes.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref readonly RawColor32 ToRaw() =>
		// tests with Godbolt confirm this is a real optimization:
		// - on CoreCLR, a byte-by-byte copy via shl/or turns into `mov eax, dword ptr [rdi]; ret`
		// - on NativeAOT, movzx-ing the four args byte-by-byte into rdi/rsi/rdx/rcx + a
		//   `call RawColor32:.ctor` turns into the same `mov eax, dword ptr [rax]`
		// it's certainly interesting that CoreCLR manages to elide the call but not the byte-by-byte copy
		ref Unsafe.As<SrgbColor32, RawColor32>(ref Unsafe.AsRef(in this));

	/// <summary>
	/// Drops the color space information of the span, without changing the bytes.
	/// </summary>
	public static ReadOnlySpan<RawColor32> SpanToRaw(ReadOnlySpan<SrgbColor32> span) =>
		MemoryMarshal.Cast<SrgbColor32, RawColor32>(span);

	/// <summary>
	/// Drops the color space information of the span, without changing the bytes.
	/// </summary>
	public static Span<RawColor32> SpanToRaw(Span<SrgbColor32> span) =>
		MemoryMarshal.Cast<SrgbColor32, RawColor32>(span);

	/// <summary>
	/// Normalizes this value to a <see cref="RawColorF128"/>, with each channel mapped from [0, 255]
	/// to [0, 1].
	/// </summary>
	/// <remarks>
	/// This doesn't decode sRGB; the result is still sRGB-encoded. This is what a non-sRGB render
	/// target expects for its clear value when the rest of the frame is drawn with sRGB-encoded
	/// values.
	/// </remarks>
	public RawColorF128 ToRawF128() => new(R / 255f, G / 255f, B / 255f, A / 255f);
}

/// <summary>
/// <see cref="JsonConverter{T}"/> for <see cref="SrgbColor32"/>s, as JSON strings holding hex
/// codes, e.g. <c>"#CBA6F7FF"</c>.
/// </summary>
public sealed class SrgbColor32JsonConverter : JsonConverter<SrgbColor32> {
	/// <summary>
	/// Reads a JSON string value and parses it with <see cref="SrgbColor32.Parse(string?)"/>, so the
	/// alpha and the leading <c>#</c> are optional.
	/// </summary>
	/// <exception cref="JsonException">
	/// Thrown if the value isn't a JSON string, or isn't a valid hex code.
	/// </exception>
	public override SrgbColor32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.String)
			throw new JsonException("expected string");
		return SrgbColor32.TryParse(reader.GetString(), out SrgbColor32 val)
			? val
			: throw new JsonException($"invalid SrgbColor32 hex code '{reader.GetString()}'");
	}

	/// <summary>
	/// Writes a <see cref="SrgbColor32"/> as a JSON string value using
	/// <see cref="SrgbColor32.ToString()"/>, i.e. in the form <c>#RRGGBBAA</c>.
	/// </summary>
	public override void Write(Utf8JsonWriter writer, SrgbColor32 val, JsonSerializerOptions options) =>
		writer.WriteStringValue(val.ToString());
}
