// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure;

/// <summary>
/// 32bpp RGBA color laid out in memory as:
/// <list type="bullet">
/// <item><description>Byte 0: <see cref="R"/></description></item>
/// <item><description>Byte 1: <see cref="G"/></description></item>
/// <item><description>Byte 2: <see cref="B"/></description></item>
/// <item><description>Byte 3: <see cref="A"/></description></item>
/// </list>
/// </summary>
/// <remarks>
/// No colorspace/premultiplied-alpha information is encoded, for obvious reasons.
/// Don't assume anything; check in with the API producing the values.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 4, Pack = 1)]
public readonly struct Color32(byte r, byte g, byte b, byte a = 0xff) : IEquatable<Color32>, ISpanParsable<Color32> {
	/// <summary>Red value, at byte offset 0.</summary>
	[FieldOffset(0)] public readonly byte R = r;
	/// <summary>Green value, at byte offset 1.</summary>
	[FieldOffset(1)] public readonly byte G = g;
	/// <summary>Blue value, at byte offset 2.</summary>
	[FieldOffset(2)] public readonly byte B = b;
	/// <summary>Alpha value, at byte offset 3.</summary>
	[FieldOffset(3)] public readonly byte A = a;

	/// <summary>Size of a <see cref="Color32"/> value in bytes. Equal to 4.</summary>
	public const int Size = 4;

#if DEBUG
	static Color32() {
		if (Unsafe.SizeOf<Color32>() != 4)
			throw new InternalStateException("expected Color32 size to be 4 bytes");
		if (Marshal.OffsetOf<Color32>(nameof(R)) != 0)
			throw new InternalStateException("expected Color32 R offset to be 0");
		if (Marshal.OffsetOf<Color32>(nameof(G)) != 1)
			throw new InternalStateException("expected Color32 G offset to be 1");
		if (Marshal.OffsetOf<Color32>(nameof(B)) != 2)
			throw new InternalStateException("expected Color32 B offset to be 2");
		if (Marshal.OffsetOf<Color32>(nameof(A)) != 3)
			throw new InternalStateException("expected Color32 A offset to be 3");
	}
#endif

	/// <summary>
	/// Reads the <paramref name="n"/>th byte of this <see cref="Color32"/> value.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="n"/> is not in the range [0, 3].
	/// </exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly byte GetByte(int n) {
		if ((uint)n >= 4)
			throw new ArgumentOutOfRangeException(nameof(n), n, "Color32 only has 4 bytes");
		ref byte b0 = ref Unsafe.As<Color32, byte>(ref Unsafe.AsRef(in this));
		return Unsafe.Add(ref b0, n);
	}

	/// <summary>
	/// Reads the <paramref name="n"/>th byte of this <see cref="Color32"/> value without
	/// checking if <paramref name="n"/> is in bounds.
	/// </summary>
	/// <remarks>
	/// The caller must make sure <paramref name="n"/> is in the range [0, 3]; other values
	/// will cause out-of-bounds reads.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly byte GetByteUnchecked(int n) {
		ref byte b0 = ref Unsafe.As<Color32, byte>(ref Unsafe.AsRef(in this));
		return Unsafe.Add(ref b0, n);
	}

	/// <summary>Produces a new <see cref="Color32"/> value with its <see cref="R"/> component replaced.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Color32 WithR(byte r) => new(r, G, B, A);

	/// <summary>Produces a new <see cref="Color32"/> value with its <see cref="G"/> component replaced.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Color32 WithG(byte g) => new(R, g, B, A);

	/// <summary>Produces a new <see cref="Color32"/> value with its <see cref="B"/> component replaced.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Color32 WithB(byte b) => new(R, G, b, A);

	/// <summary>Produces a new <see cref="Color32"/> value with its <see cref="A"/> component replaced.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Color32 WithA(byte a) => new(R, G, B, a);

	/// <summary>Converts this value to a <c>0xRRGGBBAA</c> u32 integer.</summary>
	/// <remarks>
	/// This is not the same as reinterpreting the bytes; for example, on little-endian,
	/// the resulting integer has memory layout <c>AABBGGRR</c> (when going lower -> higher memory address).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public uint ToLogicalRgba32() => (uint)R << 24 | (uint)G << 16 | (uint)B << 8 | A;

	/// <summary>Converts this value to a 0xAARRGGBB u32 integer.</summary>
	/// <remarks>
	/// This is not the same as reinterpreting the bytes; for example, on little-endian,
	/// the resulting integer has memory layout <c>BBGGRRAA</c> (when going lower -> higher memory address).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public uint ToLogicalArgb32() => (uint)A << 24 | (uint)R << 16 | (uint)G << 8 | B;

	/// <summary>Converts this value to a 0xAABBGGRR u32 integer.</summary>
	/// <remarks>
	/// This is not the same as reinterpreting the bytes; for example, on little-endian,
	/// the resulting integer has memory layout <c>RRGGBBAA</c> (when going lower -> higher memory address).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public uint ToLogicalAbgr32() => (uint)A << 24 | (uint)B << 16 | (uint)G << 8 | R;

	/// <summary>Converts this value to a <c>0xBBGGRRAA</c> u32 integer.</summary>
	/// <remarks>
	/// This is not the same as reinterpreting the bytes; for example, on little-endian,
	/// the resulting integer has memory layout <c>AARRGGBB</c> (when going lower -> higher memory address).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public uint ToLogicalBgra32() => (uint)B << 24 | (uint)G << 16 | (uint)R << 8 | A;

	/// <summary>Converts a 0xRRGGBBAA u32 integer to a <see cref="Color32"/> value.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Color32 FromLogicalRgba32(uint rgba) => new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);

	/// <summary>Converts a 0xAARRGGBB u32 integer to a <see cref="Color32"/> value.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Color32 FromLogicalArgb32(uint argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

	/// <summary>Converts a 0xAABBGGRR u32 integer to a <see cref="Color32"/> value.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Color32 FromLogicalAbgr32(uint abgr) => new((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16), (byte)(abgr >> 24));

	/// <summary>Converts a <c>0xBBGGRRAA</c> u32 integer to a <see cref="Color32"/> value.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Color32 FromLogicalBgra32(uint bgra) => new((byte)(bgra >> 8), (byte)(bgra >> 16), (byte)(bgra >> 24), (byte)bgra);

	/// <summary>
	/// Reinterprets this value as a u32 integer; the result is host-endianness-dependent.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public uint ReinterpretToU32() =>
		Unsafe.ReadUnaligned<uint>(ref Unsafe.As<Color32, byte>(ref Unsafe.AsRef(in this))); // do an unaligned read since the min alignment of this struct is 1

	/// <summary>
	/// Reinterprets a u32 integer as a <see cref="Color32"/> value; the result is host-endianness-dependent.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Color32 ReinterpretFromU32(uint v) => Unsafe.As<uint, Color32>(ref v);

	/// <summary>
	/// Reads a 4-byte RGBA value from the provided span as a <see cref="Color32"/> value.
	/// </summary>
	/// <param name="bytes">Span to read from.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="bytes"/> is not at least 4 bytes in length.
	/// </exception>
	public static Color32 FromRgbaBytes(ReadOnlySpan<byte> bytes) {
		if (bytes.Length < 4)
			throw new ArgumentException("expected at least 4 bytes", nameof(bytes));
		return new Color32(bytes[0], bytes[1], bytes[2], bytes[3]);
	}

	/// <summary>
	/// Attempts to read a 4-byte RGBA value from the provided span as a <see cref="Color32"/> value.
	/// </summary>
	/// <param name="bytes">Span to read from.</param>
	/// <param name="color">On success, the read value.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="bytes"/> is at least 4 bytes in length and as such
	/// a value was successfully read, and <see langword="false"/> otherwise.
	/// </returns>
	public static bool TryFromRgbaBytes(ReadOnlySpan<byte> bytes, out Color32 color) {
		if (bytes.Length < 4) {
			color = default;
			return false;
		}
		color = new Color32(bytes[0], bytes[1], bytes[2], bytes[3]);
		return true;
	}

	/// <summary>
	/// Writes this value as a 4-byte RGBA value into the provided span.
	/// </summary>
	/// <param name="dst">Span to write to.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="dst"/> is not at least 4 bytes in length.
	/// </exception>
	public void WriteRgbaBytes(Span<byte> dst) {
		if (dst.Length < 4)
			throw new ArgumentException("expected at least 4 bytes", nameof(dst));
		dst[0] = R;
		dst[1] = G;
		dst[2] = B;
		dst[3] = A;
	}

	/// <summary>
	/// Attempts to write this value as a 4-byte RGBA value into the provided span.
	/// </summary>
	/// <param name="dst">Span to write to.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="dst"/> is at least 4 bytes in length and as such
	/// a value was successfully written, and <see langword="false"/> otherwise.
	/// </returns>
	public bool TryWriteRgbaBytes(Span<byte> dst) {
		if (dst.Length < 4)
			return false;
		dst[0] = R;
		dst[1] = G;
		dst[2] = B;
		dst[3] = A;
		return true;
	}

	/// <summary>
	/// Writes the R/G/B/A values to the corresponding parameters.
	/// </summary>
	public void Deconstruct(out byte r, out byte g, out byte b, out byte a) {
		r = R;
		g = G;
		b = B;
		a = A;
	}

	/// <summary>
	/// Writes the R/G/B values to the corresponding parameters.
	/// </summary>
	public void Deconstruct(out byte r, out byte g, out byte b) {
		r = R;
		g = G;
		b = B;
	}

	/// <summary>
	/// Converts this value to a <see cref="Vector4"/> with each channel in the range [0, 1].
	/// </summary>
	public Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);

	/// <summary>
	/// Converts this value to a <see cref="WebGPU.WGPUColor"/> with each channel in the range [0, 1].
	/// </summary>
	internal WebGPU.WGPUColor ToWebGPUColor() => new(R / 255.0, G / 255.0, B / 255.0, A / 255.0);

	public bool Equals(Color32 other) => R == other.R && G == other.G && B == other.B && A == other.A;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is Color32 other && Equals(other);
	public override int GetHashCode() => unchecked((int)ToLogicalRgba32());
	public static bool operator ==(Color32 left, Color32 right) => left.Equals(right);
	public static bool operator !=(Color32 left, Color32 right) => !left.Equals(right);

	/// <summary>
	/// Converts this value to a hex code in the format <c>RRGGBBAA</c>.
	/// </summary>
	/// <remarks>
	/// Currently equivalent to <see cref="ToHexCode(bool, bool)"/> with the default parameter values.
	/// </remarks>
	public override string ToString() => ToHexCode(includeAlpha: true, leadingHash: false);

	/// <summary>
	/// Converts this value to a hex code in the format <c>RRGGBB</c> or <c>RRGGBBAA</c>,
	/// optionally with a leading <c>#</c> character.
	/// </summary>
	/// <param name="includeAlpha">Whether the alpha byte should be included. <see langword="false"/> will lose alpha information.</param>
	/// <param name="leadingHash">Whether a leading <c>#</c> character should be added.</param>
	public string ToHexCode(bool includeAlpha = true, bool leadingHash = false) {
		string prefix = leadingHash ? "#" : "";
		return includeAlpha ? $"{prefix}{R:X2}{G:X2}{B:X2}{A:X2}" : $"{prefix}{R:X2}{G:X2}{B:X2}";
	}

	/// <summary>
	/// Parses a <see cref="Color32"/> from a hex-code string. Alpha may be omitted to mean opaque, and
	/// an optional leading <c>#</c> character is accepted.
	/// </summary>
	/// <param name="span">Span of characters to parse.</param>
	/// <param name="val">On success, the parsed value.</param>
	/// <returns>
	/// <see langword="true"/> if the string is in the format <c>RRGGBB</c> or <c>RRGGBBAA</c>,
	/// optionally with a leading <c>#</c>, and as such the parse succeeded; otherwise <see langword="false"/>.
	/// </returns>
	public static bool TryParse(ReadOnlySpan<char> span, out Color32 val) {
		static bool conv(char c, out byte v) {
			v = 0;
			if (c >= '0' && c <= '9')
				v = (byte)(c - '0');
			else if (c >= 'a' && c <= 'f')
				v = (byte)(c - 'a' + 0xa);
			else if (c >= 'A' && c <= 'F')
				v = (byte)(c - 'A' + 0xa);
			else
				return false;
			return true;
		}

		val = default;
		int n = 0;
		if (span.Length >= 1 && span[0] == '#')
			n++;
		if (span.Length - n != 6 && span.Length - n != 8)
			return false;
		if (!conv(span[n], out byte r0) || !conv(span[n + 1], out byte r1) || !conv(span[n + 2], out byte g0) || !conv(span[n + 3], out byte g1) || !conv(span[n + 4], out byte b0) ||
			!conv(span[n + 5], out byte b1))
			return false;

		byte r = (byte)(r0 << 4 | r1);
		byte g = (byte)(g0 << 4 | g1);
		byte b = (byte)(b0 << 4 | b1);
		byte a = 0xff;
		if (span.Length - n == 8) {
			if (!conv(span[n + 6], out byte a0) || !conv(span[n + 7], out byte a1))
				return false;
			a = (byte)(a0 << 4 | a1);
		}
		val = new Color32(r, g, b, a);
		return true;
	}
	/// <inheritdoc cref="TryParse(ReadOnlySpan{char}, out Color32)"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="ISpanParsable{TSelf}"/>.
	/// </remarks>
	public static bool TryParse(ReadOnlySpan<char> span, IFormatProvider? provider, out Color32 val) => TryParse(span, out val);

	/// <summary>
	/// Parses a <see cref="Color32"/> from a hex-code string. Alpha may be omitted to mean opaque, and
	/// an optional leading <c>#</c> character is accepted.
	/// </summary>
	/// <param name="span">Span of characters to parse.</param>
	/// <returns>
	/// The parsed value.
	/// </returns>
	/// <exception cref="FormatException">
	/// Thrown if the string is not in the format <c>RRGGBB</c> or <c>RRGGBBAA</c>, optionally with a leading <c>#</c>.
	/// </exception>
	public static Color32 Parse(ReadOnlySpan<char> span) {
		if (!TryParse(span, out Color32 val))
			throw new FormatException("expected RRGGBB or RRGGBBAA with optional leading #");
		return val;
	}
	/// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="ISpanParsable{TSelf}"/>.
	/// </remarks>
	public static Color32 Parse(ReadOnlySpan<char> span, IFormatProvider? provider) => Parse(span);

	/// <inheritdoc cref="TryParse(ReadOnlySpan{char}, out Color32)"/>
	/// <param name="s">String to parse.</param>
	/// <param name="val">On success, the parsed value.</param>
	public static bool TryParse([NotNullWhen(true)] string? s, out Color32 val) => TryParse(s.AsSpan(), out val);

	/// <inheritdoc cref="TryParse(string?, out Color32)"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Color32 val) => TryParse(s.AsSpan(), out val);

	/// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
	/// <param name="s">String to parse.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="s"/> is <see langword="null"/>.
	/// </exception>
	public static Color32 Parse([NotNull] string? s) => Parse((s ?? throw new ArgumentNullException(nameof(s))).AsSpan());

	/// <inheritdoc cref="Parse(string?)"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static Color32 Parse([NotNull] string? s, IFormatProvider? provider) => Parse((s ?? throw new ArgumentNullException(nameof(s))).AsSpan());

	/// <summary>The color <c>#00000000</c>.</summary>
	public static readonly Color32 Transparent = new(0x00, 0x00, 0x00, 0x00);
	/// <summary>The color <c>#FFFFFFFF</c>.</summary>
	public static readonly Color32 White = new(0xff, 0xff, 0xff, 0xff);
	/// <summary>The color <c>#000000FF</c>.</summary>
	public static readonly Color32 Black = new(0x00, 0x00, 0x00, 0xff);
	/// <summary>The color <c>#FF0000FF</c>.</summary>
	public static readonly Color32 Red = new(0xff, 0x00, 0x00, 0xff);
	/// <summary>The color <c>#00FF00FF</c>.</summary>
	public static readonly Color32 Green = new(0x00, 0xff, 0x00, 0xff);
	/// <summary>The color <c>#0000FFFF</c>.</summary>
	public static readonly Color32 Blue = new(0x00, 0x00, 0xff, 0xff);
	/// <summary>The color <c>#FFFF00FF</c>.</summary>
	public static readonly Color32 Yellow = new(0xff, 0xff, 0x00, 0xff);
	/// <summary>The color <c>#00FFFFFF</c>.</summary>
	public static readonly Color32 Cyan = new(0x00, 0xff, 0xff, 0xff);
	/// <summary>The color <c>#FF00FFFF</c>.</summary>
	public static readonly Color32 Magenta = new(0xff, 0x00, 0xff, 0xff);
	/// <summary>The color <c>#00008BFF</c>.</summary>
	public static readonly Color32 DarkBlue = new(0x00, 0x00, 0x8b, 0xff);
}
