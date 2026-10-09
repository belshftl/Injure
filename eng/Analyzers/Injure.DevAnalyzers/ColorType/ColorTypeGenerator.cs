// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Text;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.DevAnalyzers.ColorType;

// emits the members shared by every color type of a kind, so that e.g. RawColor32 and SrgbColor32
// can't drift apart; the hand-written part of each type has the summary (but no remarks, since the
// compiler concats the doc comments of all parts) and conversions
[Generator]
public sealed class ColorTypeGenerator : IIncrementalGenerator {
	// ==========================================================================
	// internal types
	private sealed class TargetInfo(INamedTypeSymbol symbol, ColorTypeKind kind) {
		public INamedTypeSymbol Symbol { get; } = symbol;
		public ColorTypeKind Kind { get; } = kind;
	}

	// ==========================================================================
	// IIncrementalGenerator
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		SourceGen.RegisterPostInitializationSources(
			context,
			(Constants.ColorType.Color32AttributeFilename, Constants.ColorType.Color32AttributeSource),
			(Constants.ColorType.ColorF128AttributeFilename, Constants.ColorType.ColorF128AttributeSource)
		);
		register(context, Constants.ColorType.Color32AttributeMetadataName, ColorTypeKind.Color32);
		register(context, Constants.ColorType.ColorF128AttributeMetadataName, ColorTypeKind.ColorF128);
	}

	private static void register(IncrementalGeneratorInitializationContext context, string metadataName, ColorTypeKind kind) {
		IncrementalValuesProvider<TargetInfo?> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			metadataName,
			predicate: static (node, _) => node is StructDeclarationSyntax,
			transform: (ctx, ct) => {
				var sym = (INamedTypeSymbol)ctx.TargetSymbol;
				return ColorTypeModel.Validate(sym, ctx.Attributes[0], kind, DiagnosticSink.Silent(), ct)
					? new TargetInfo(sym, kind)
					: null;
			}
		);
		SourceGen.RegisterTargetOutput(context, targets, static info => info.Symbol, Constants.ColorType.GeneratedSourceSuffix, emit);
	}

	// ==========================================================================
	// source emission
	private static string emit(TargetInfo info) {
		StringBuilder sb = new();
		SourceGen.AppendFileHeader(sb);
		SourceGen.OpenScopes(sb, info.Symbol);
		string self = Util.EscapeIdentifier(info.Symbol.Name);
		string header = Util.GetTypeHeader(info.Symbol);
		string body = info.Kind switch {
			ColorTypeKind.Color32 => color32Template,
			ColorTypeKind.ColorF128 => colorF128Template,
			_ => throw new ArgumentOutOfRangeException(nameof(info)),
		};
		body = body.Replace("$Self$", self).Replace("$Header$", header);
		sb.Append(body.Replace("$GeneratedCode$", generatedCodeAttribute()));
		SourceGen.CloseScopes(sb, info.Symbol);
		return sb.ToString();
	}

	private static string generatedCodeAttribute() {
		StringBuilder sb = new();
		SourceGen.AppendGeneratedCodeAttribute(sb, nameof(ColorTypeGenerator));
		return sb.ToString().TrimEnd();
	}

	// ==========================================================================
	// templates
	private const string color32Template = """
		/// <remarks>
		/// <para>
		/// Laid out in memory as bytes <see cref="R"/>, <see cref="G"/>, <see cref="B"/>, <see cref="A"/>,
		/// in that order. Intended for both usage as a general type and as an unmanaged, blittable
		/// primitive. The memory layout is fixed, not incidental. Safe for FFI, <c>stackalloc $Self$[]</c>,
		/// bit/span reinterprets, etc.
		/// </para>
		/// <para>
		/// All currently supported .NET runtime targets are little-endian, so the bit pattern of the packed
		/// <see cref="uint"/> equivalent (as provided by e.g. <see cref="ReinterpretToU32()"/>) is always
		/// <c>0xAABBGGRR</c>. This is a platform observation, not a guarantee; the .NET runtime may support
		/// big-endian targets in the future.
		/// </para>
		/// <para>
		/// No premultiplied-alpha information is encoded. Check in with the API producing the values.
		/// </para>
		/// <para>
		/// The <see langword="default"/> value is valid and is <see cref="Transparent"/>.
		/// </para>
		/// </remarks>
		$GeneratedCode$
		[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = 4, Pack = 1)]
		$Header$ : global::System.IEquatable<$Self$>, global::System.ISpanParsable<$Self$> {
			/// <summary>
			/// Red value, at byte offset 0.
			/// </summary>
			[global::System.Runtime.InteropServices.FieldOffset(0)] public readonly byte R;
			/// <summary>
			/// Green value, at byte offset 1.
			/// </summary>
			[global::System.Runtime.InteropServices.FieldOffset(1)] public readonly byte G;
			/// <summary>
			/// Blue value, at byte offset 2.
			/// </summary>
			[global::System.Runtime.InteropServices.FieldOffset(2)] public readonly byte B;
			/// <summary>
			/// Alpha value, at byte offset 3.
			/// </summary>
			[global::System.Runtime.InteropServices.FieldOffset(3)] public readonly byte A;

			/// <summary>
			/// Size of a <see cref="$Self$"/> value in bytes. Equal to 4.
			/// </summary>
			public const int Size = 4;

			/// <summary>
			/// Creates a <see cref="$Self$"/> value from its components.
			/// </summary>
			public $Self$(byte r, byte g, byte b, byte a = 0xff) {
				R = r;
				G = g;
				B = b;
				A = a;
			}

		#if DEBUG
			static $Self$() {
				if (global::System.Runtime.CompilerServices.Unsafe.SizeOf<$Self$>() != Size)
					throw new global::Injure.InternalStateException("expected $Self$ size to be 4 bytes");
				if (global::System.Runtime.InteropServices.Marshal.OffsetOf<$Self$>(nameof(R)) != 0)
					throw new global::Injure.InternalStateException("expected $Self$ R offset to be 0");
				if (global::System.Runtime.InteropServices.Marshal.OffsetOf<$Self$>(nameof(G)) != 1)
					throw new global::Injure.InternalStateException("expected $Self$ G offset to be 1");
				if (global::System.Runtime.InteropServices.Marshal.OffsetOf<$Self$>(nameof(B)) != 2)
					throw new global::Injure.InternalStateException("expected $Self$ B offset to be 2");
				if (global::System.Runtime.InteropServices.Marshal.OffsetOf<$Self$>(nameof(A)) != 3)
					throw new global::Injure.InternalStateException("expected $Self$ A offset to be 3");
			}
		#endif

			/// <summary>
			/// Reads the <paramref name="n"/>th byte of this <see cref="$Self$"/> value without
			/// checking if <paramref name="n"/> is in bounds.
			/// </summary>
			/// <remarks>
			/// <para>
			/// The caller must make sure <paramref name="n"/> is in the range [0, 3]; other values
			/// will cause out-of-bounds reads.
			/// </para>
			/// <para>
			/// A test under Godbolt, .NET 10.0 CoreCLR, produced:
			/// <code>
			/// movzx rax, byte ptr [rdi+rsi]
			/// ret
			/// </code>
			/// Obviously, no API guarantees can be made about what this gets compiled / JIT'ed to;
			/// this is merely a remark about the purpose of this method and whether using it incurs
			/// significant overhead or not.
			/// </para>
			/// </remarks>
			[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			public readonly byte GetByteUnchecked(nuint n) {
				ref byte b0 = ref global::System.Runtime.CompilerServices.Unsafe.As<$Self$, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in this));
				return global::System.Runtime.CompilerServices.Unsafe.Add(ref b0, n);
			}

			/// <summary>
			/// Produces a new <see cref="$Self$"/> value with its <see cref="R"/> component replaced.
			/// </summary>
			public $Self$ WithR(byte r) => new(r, G, B, A);

			/// <summary>
			/// Produces a new <see cref="$Self$"/> value with its <see cref="G"/> component replaced.
			/// </summary>
			public $Self$ WithG(byte g) => new(R, g, B, A);

			/// <summary>
			/// Produces a new <see cref="$Self$"/> value with its <see cref="B"/> component replaced.
			/// </summary>
			public $Self$ WithB(byte b) => new(R, G, b, A);

			/// <summary>
			/// Produces a new <see cref="$Self$"/> value with its <see cref="A"/> component replaced.
			/// </summary>
			public $Self$ WithA(byte a) => new(R, G, B, a);

			/// <summary>
			/// Converts this value to a <c>0xRRGGBBAA</c> u32 integer.
			/// </summary>
			/// <remarks>
			/// <para>
			/// This is not the same as reinterpreting the bytes; for example, on little-endian, the resulting
			/// integer has memory layout <c>AABBGGRR</c> (when going lower -> higher memory address).
			/// </para>
			/// <para>
			/// Since all currently supported .NET runtime targets are little-endian (see remark on
			/// <see cref="$Self$"/>), this happens to actually be the same as reinterpreting the bytes. It
			/// will, however, not be the same if the runtime ever supports big-endian targets.
			/// </para>
			/// </remarks>
			public uint ToLogicalRgba32() => (uint)R << 24 | (uint)G << 16 | (uint)B << 8 | A;

			/// <summary>
			/// Converts this value to a 0xAARRGGBB u32 integer.
			/// </summary>
			/// <remarks>
			/// This is not the same as reinterpreting the bytes; for example, on little-endian, the resulting
			/// integer has memory layout <c>BBGGRRAA</c> (when going lower -> higher memory address).
			/// </remarks>
			public uint ToLogicalArgb32() => (uint)A << 24 | (uint)R << 16 | (uint)G << 8 | B;

			/// <summary>
			/// Converts this value to a 0xAABBGGRR u32 integer.
			/// </summary>
			/// <remarks>
			/// This is not the same as reinterpreting the bytes; for example, on little-endian, the resulting
			/// integer has memory layout <c>RRGGBBAA</c> (when going lower -> higher memory address).
			/// </remarks>
			public uint ToLogicalAbgr32() => (uint)A << 24 | (uint)B << 16 | (uint)G << 8 | R;

			/// <summary>
			/// Converts this value to a <c>0xBBGGRRAA</c> u32 integer.
			/// </summary>
			/// <remarks>
			/// This is not the same as reinterpreting the bytes; for example, on little-endian, the resulting
			/// integer has memory layout <c>AARRGGBB</c> (when going lower -> higher memory address).
			/// </remarks>
			public uint ToLogicalBgra32() => (uint)B << 24 | (uint)G << 16 | (uint)R << 8 | A;

			/// <summary>
			/// Converts a 0xRRGGBBAA u32 integer to a <see cref="$Self$"/> value.
			/// </summary>
			/// <remarks>
			/// Since all currently supported .NET runtime targets are little-endian (see remark on
			/// <see cref="$Self$"/>), this happens to be the same as reinterpreting the bytes. It will,
			/// however, not be the same if the runtime ever supports big-endian targets.
			/// </remarks>
			public static $Self$ FromLogicalRgba32(uint rgba) => new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);

			/// <summary>
			/// Converts a 0xAARRGGBB u32 integer to a <see cref="$Self$"/> value.
			/// </summary>
			public static $Self$ FromLogicalArgb32(uint argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

			/// <summary>
			/// Converts a 0xAABBGGRR u32 integer to a <see cref="$Self$"/> value.
			/// </summary>
			public static $Self$ FromLogicalAbgr32(uint abgr) => new((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16), (byte)(abgr >> 24));

			/// <summary>
			/// Converts a <c>0xBBGGRRAA</c> u32 integer to a <see cref="$Self$"/> value.
			/// </summary>
			public static $Self$ FromLogicalBgra32(uint bgra) => new((byte)(bgra >> 8), (byte)(bgra >> 16), (byte)(bgra >> 24), (byte)bgra);

			/// <summary>
			/// Reinterprets this value as a u32 integer; the result is host-endianness-dependent.
			/// </summary>
			[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			public uint ReinterpretToU32() =>
				// do an unaligned read since the min alignment of this struct is 1
				global::System.Runtime.CompilerServices.Unsafe.ReadUnaligned<uint>(
					ref global::System.Runtime.CompilerServices.Unsafe.As<$Self$, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in this))
				);

			/// <summary>
			/// Reinterprets a u32 integer as a <see cref="$Self$"/> value; the result is
			/// host-endianness-dependent.
			/// </summary>
			[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			public static $Self$ ReinterpretFromU32(uint v) => global::System.Runtime.CompilerServices.Unsafe.As<uint, $Self$>(ref v);

			/// <summary>
			/// Reads a 4-byte RGBA value from the provided span as a <see cref="$Self$"/> value.
			/// </summary>
			/// <param name="bytes">Span to read from.</param>
			/// <exception cref="global::System.ArgumentException">
			/// Thrown if <paramref name="bytes"/> is not at least 4 bytes in length.
			/// </exception>
			public static $Self$ FromRgbaBytes(global::System.ReadOnlySpan<byte> bytes) {
				if (bytes.Length < 4)
					throw new global::System.ArgumentException("expected at least 4 bytes", nameof(bytes));
				return new $Self$(bytes[0], bytes[1], bytes[2], bytes[3]);
			}

			/// <summary>
			/// Attempts to read a 4-byte RGBA value from the provided span as a <see cref="$Self$"/> value.
			/// </summary>
			/// <param name="bytes">Span to read from.</param>
			/// <param name="color">On success, the read value.</param>
			/// <returns>
			/// <see langword="true"/> if <paramref name="bytes"/> is at least 4 bytes in length and as such a
			/// value was successfully read, and <see langword="false"/> otherwise.
			/// </returns>
			public static bool TryFromRgbaBytes(global::System.ReadOnlySpan<byte> bytes, out $Self$ color) {
				if (bytes.Length < 4) {
					color = default;
					return false;
				}
				color = new $Self$(bytes[0], bytes[1], bytes[2], bytes[3]);
				return true;
			}

			/// <summary>
			/// Writes this value as a 4-byte RGBA value into the provided span.
			/// </summary>
			/// <param name="dst">Span to write to.</param>
			/// <exception cref="global::System.ArgumentException">
			/// Thrown if <paramref name="dst"/> is not at least 4 bytes in length.
			/// </exception>
			public void WriteRgbaBytes(global::System.Span<byte> dst) {
				if (dst.Length < 4)
					throw new global::System.ArgumentException("expected at least 4 bytes", nameof(dst));
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
			/// <see langword="true"/> if <paramref name="dst"/> is at least 4 bytes in length and as such a
			/// value was successfully written, and <see langword="false"/> otherwise.
			/// </returns>
			public bool TryWriteRgbaBytes(global::System.Span<byte> dst) {
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

			public bool Equals($Self$ other) => R == other.R && G == other.G && B == other.B && A == other.A;
			public override bool Equals([global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is $Self$ other && Equals(other);
			public override int GetHashCode() => unchecked((int)ToLogicalRgba32());
			public static bool operator ==($Self$ left, $Self$ right) => left.Equals(right);
			public static bool operator !=($Self$ left, $Self$ right) => !left.Equals(right);

			/// <summary>
			/// Formats this value to a hex code in the form <c>RRGGBBAA</c>.
			/// </summary>
			/// <remarks>
			/// Currently equivalent to <see cref="ToHexCode(bool, bool)"/> with the default parameter values.
			/// </remarks>
			public override string ToString() => ToHexCode(includeAlpha: true, leadingHash: false);

			/// <summary>
			/// Converts this value to a hex code in the format <c>RRGGBB</c> or <c>RRGGBBAA</c>,
			/// optionally with a leading <c>#</c> character.
			/// </summary>
			/// <param name="includeAlpha">
			/// Whether the alpha byte should be included. <see langword="false"/> will lose alpha information.
			/// </param>
			/// <param name="leadingHash">Whether a leading <c>#</c> character should be added.</param>
			public string ToHexCode(bool includeAlpha = true, bool leadingHash = false) {
				string prefix = leadingHash ? "#" : "";
				return includeAlpha ? $"{prefix}{R:X2}{G:X2}{B:X2}{A:X2}" : $"{prefix}{R:X2}{G:X2}{B:X2}";
			}

			/// <summary>
			/// Parses a <see cref="$Self$"/> from a hex-code string. Alpha may be omitted to mean opaque, and
			/// an optional leading <c>#</c> character is accepted. Case-insensitive.
			/// </summary>
			/// <param name="span">Span of characters to parse.</param>
			/// <param name="val">On success, the parsed value.</param>
			/// <returns>
			/// <see langword="true"/> if the string is in the format <c>RRGGBB</c> or <c>RRGGBBAA</c>,
			/// optionally with a leading <c>#</c>, and as such the parse succeeded; otherwise
			/// <see langword="false"/>.
			/// </returns>
			public static bool TryParse(global::System.ReadOnlySpan<char> span, out $Self$ val) {
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
				if (!conv(span[n], out byte r0) || !conv(span[n + 1], out byte r1) || !conv(span[n + 2], out byte g0) ||
					!conv(span[n + 3], out byte g1) || !conv(span[n + 4], out byte b0) || !conv(span[n + 5], out byte b1))
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
				val = new $Self$(r, g, b, a);
				return true;
			}
			/// <inheritdoc cref="TryParse(global::System.ReadOnlySpan{char}, out $Self$)"/>
			/// <remarks>
			/// <paramref name="provider"/> is ignored and is for compatibility with
			/// <see cref="global::System.ISpanParsable{TSelf}"/>.
			/// </remarks>
			public static bool TryParse(global::System.ReadOnlySpan<char> span, global::System.IFormatProvider? provider, out $Self$ val) =>
				TryParse(span, out val);

			/// <summary>
			/// Parses a <see cref="$Self$"/> from a hex-code string. Alpha may be omitted to mean opaque, and
			/// an optional leading <c>#</c> character is accepted. Case-insensitive.
			/// </summary>
			/// <param name="span">Span of characters to parse.</param>
			/// <returns>
			/// The parsed value.
			/// </returns>
			/// <exception cref="global::System.FormatException">
			/// Thrown if the string is not in the format <c>RRGGBB</c> or <c>RRGGBBAA</c>, optionally with a
			/// leading <c>#</c>.
			/// </exception>
			public static $Self$ Parse(global::System.ReadOnlySpan<char> span) {
				if (!TryParse(span, out $Self$ val))
					throw new global::System.FormatException("expected RRGGBB or RRGGBBAA with optional leading #");
				return val;
			}
			/// <inheritdoc cref="Parse(global::System.ReadOnlySpan{char})"/>
			/// <remarks>
			/// <paramref name="provider"/> is ignored and is for compatibility with
			/// <see cref="global::System.ISpanParsable{TSelf}"/>.
			/// </remarks>
			public static $Self$ Parse(global::System.ReadOnlySpan<char> span, global::System.IFormatProvider? provider) => Parse(span);

			/// <inheritdoc cref="TryParse(global::System.ReadOnlySpan{char}, out $Self$)"/>
			/// <param name="s">String to parse.</param>
			/// <param name="val">On success, the parsed value.</param>
			public static bool TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out $Self$ val) =>
				TryParse(global::System.MemoryExtensions.AsSpan(s), out val);

			/// <inheritdoc cref="TryParse(string?, out $Self$)"/>
			/// <remarks>
			/// <paramref name="provider"/> is ignored and is for compatibility with
			/// <see cref="global::System.IParsable{TSelf}"/>.
			/// </remarks>
			public static bool TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, global::System.IFormatProvider? provider, out $Self$ val) =>
				TryParse(global::System.MemoryExtensions.AsSpan(s), out val);

			/// <inheritdoc cref="Parse(global::System.ReadOnlySpan{char})"/>
			/// <param name="s">String to parse.</param>
			/// <exception cref="global::System.ArgumentNullException">
			/// Thrown if <paramref name="s"/> is <see langword="null"/>.
			/// </exception>
			public static $Self$ Parse([global::System.Diagnostics.CodeAnalysis.NotNull] string? s) =>
				Parse(global::System.MemoryExtensions.AsSpan(s ?? throw new global::System.ArgumentNullException(nameof(s))));

			/// <inheritdoc cref="Parse(string?)"/>
			/// <remarks>
			/// <paramref name="provider"/> is ignored and is for compatibility with
			/// <see cref="global::System.IParsable{TSelf}"/>.
			/// </remarks>
			public static $Self$ Parse([global::System.Diagnostics.CodeAnalysis.NotNull] string? s, global::System.IFormatProvider? provider) =>
				Parse(global::System.MemoryExtensions.AsSpan(s ?? throw new global::System.ArgumentNullException(nameof(s))));

			/// <summary>
			/// The color <c>#00000000</c>.
			/// </summary>
			public static readonly $Self$ Transparent = new(0x00, 0x00, 0x00, 0x00);
			/// <summary>
			/// The color <c>#FFFFFFFF</c>.
			/// </summary>
			public static readonly $Self$ White = new(0xff, 0xff, 0xff, 0xff);
			/// <summary>
			/// The color <c>#000000FF</c>.
			/// </summary>
			public static readonly $Self$ Black = new(0x00, 0x00, 0x00, 0xff);
			/// <summary>
			/// The color <c>#FF0000FF</c>.
			/// </summary>
			public static readonly $Self$ Red = new(0xff, 0x00, 0x00, 0xff);
			/// <summary>
			/// The color <c>#00FF00FF</c>.
			/// </summary>
			public static readonly $Self$ Green = new(0x00, 0xff, 0x00, 0xff);
			/// <summary>
			/// The color <c>#0000FFFF</c>.
			/// </summary>
			public static readonly $Self$ Blue = new(0x00, 0x00, 0xff, 0xff);
			/// <summary>
			/// The color <c>#FFFF00FF</c>.
			/// </summary>
			public static readonly $Self$ Yellow = new(0xff, 0xff, 0x00, 0xff);
			/// <summary>
			/// The color <c>#00FFFFFF</c>.
			/// </summary>
			public static readonly $Self$ Cyan = new(0x00, 0xff, 0xff, 0xff);
			/// <summary>
			/// The color <c>#FF00FFFF</c>.
			/// </summary>
			public static readonly $Self$ Magenta = new(0xff, 0x00, 0xff, 0xff);
			/// <summary>
			/// The color <c>#00008BFF</c>.
			/// </summary>
			public static readonly $Self$ DarkBlue = new(0x00, 0x00, 0x8b, 0xff);
		}
		""";

	private const string colorF128Template = """
		/// <remarks>
		/// <para>
		/// Laid out in memory as <see cref="float"/>s <see cref="R"/>, <see cref="G"/>, <see cref="B"/>,
		/// <see cref="A"/>, in that order, matching a WGSL <c>vec4&lt;f32&gt;</c>. The memory layout is
		/// fixed, not incidental. Safe for FFI, <c>stackalloc $Self$[]</c>, bit/span reinterprets, etc.
		/// </para>
		/// <para>
		/// Components are not clamped, so values outside [0, 1] (e.g. for HDR formats) are representable.
		/// Components are also not validated to be finite; since this is a blittable primitive, meaningful
		/// "validation" cannot happen. As such, a component may be NaN or infinite.
		/// </para>
		/// <para>
		/// No premultiplied-alpha information is encoded. Check in with the API producing the values.
		/// </para>
		/// <para>
		/// The <see langword="default"/> value is valid and is <see cref="Transparent"/>.
		/// </para>
		/// </remarks>
		$GeneratedCode$
		[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential, Size = 16, Pack = 4)]
		$Header$ : global::System.IEquatable<$Self$> {
			/// <summary>
			/// Red value, at byte offset 0.
			/// </summary>
			public readonly float R;
			/// <summary>
			/// Green value, at byte offset 4.
			/// </summary>
			public readonly float G;
			/// <summary>
			/// Blue value, at byte offset 8.
			/// </summary>
			public readonly float B;
			/// <summary>
			/// Alpha value, at byte offset 12.
			/// </summary>
			public readonly float A;

			/// <summary>
			/// Size of a <see cref="$Self$"/> value in bytes. Equal to 16.
			/// </summary>
			public const int Size = 16;

			/// <summary>
			/// Creates a <see cref="$Self$"/> value from its components.
			/// </summary>
			public $Self$(float r, float g, float b, float a = 1f) {
				R = r;
				G = g;
				B = b;
				A = a;
			}

		#if DEBUG
			static $Self$() {
				if (global::System.Runtime.CompilerServices.Unsafe.SizeOf<$Self$>() != Size)
					throw new global::Injure.InternalStateException("expected $Self$ size to be 16 bytes");
			}
		#endif

			/// <summary>
			/// Produces a new <see cref="$Self$"/> value with its <see cref="R"/> component replaced.
			/// </summary>
			public $Self$ WithR(float r) => new(r, G, B, A);

			/// <summary>
			/// Produces a new <see cref="$Self$"/> value with its <see cref="G"/> component replaced.
			/// </summary>
			public $Self$ WithG(float g) => new(R, g, B, A);

			/// <summary>
			/// Produces a new <see cref="$Self$"/> value with its <see cref="B"/> component replaced.
			/// </summary>
			public $Self$ WithB(float b) => new(R, G, b, A);

			/// <summary>
			/// Produces a new <see cref="$Self$"/> value with its <see cref="A"/> component replaced.
			/// </summary>
			public $Self$ WithA(float a) => new(R, G, B, a);

			/// <summary>
			/// Creates a <see cref="$Self$"/> value from a <see cref="global::System.Numerics.Vector4"/>,
			/// taking <c>X</c>/<c>Y</c>/<c>Z</c>/<c>W</c> as
			/// <see cref="R"/>/<see cref="G"/>/<see cref="B"/>/<see cref="A"/>.
			/// </summary>
			public static $Self$ FromVector4(global::System.Numerics.Vector4 v) => new(v.X, v.Y, v.Z, v.W);

			/// <summary>
			/// Converts this value to a <see cref="global::System.Numerics.Vector4"/>, with
			/// <see cref="R"/>/<see cref="G"/>/<see cref="B"/>/<see cref="A"/> as
			/// <c>X</c>/<c>Y</c>/<c>Z</c>/<c>W</c>.
			/// </summary>
			public global::System.Numerics.Vector4 ToVector4() => new(R, G, B, A);

			/// <summary>
			/// Writes the R/G/B/A values to the corresponding parameters.
			/// </summary>
			public void Deconstruct(out float r, out float g, out float b, out float a) {
				r = R;
				g = G;
				b = B;
				a = A;
			}

			/// <summary>
			/// Writes the R/G/B values to the corresponding parameters.
			/// </summary>
			public void Deconstruct(out float r, out float g, out float b) {
				r = R;
				g = G;
				b = B;
			}

			// so as it turns out, float.Equals does actually have reflexivity, i.e. NaN == NaN
			// only float's equality operator diverges from that
			public bool Equals($Self$ other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
			public override bool Equals([global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is $Self$ other && Equals(other);
			public override int GetHashCode() => global::System.HashCode.Combine(R, G, B, A);
			public static bool operator ==($Self$ left, $Self$ right) => left.Equals(right);
			public static bool operator !=($Self$ left, $Self$ right) => !left.Equals(right);

			/// <summary>
			/// Formats this value to a string in the form <c>(R, G, B, A)</c>, using the invariant culture.
			/// </summary>
			public override string ToString() => global::System.FormattableString.Invariant($"({R}, {G}, {B}, {A})");

			/// <summary>
			/// The color (0, 0, 0, 0).
			/// </summary>
			public static readonly $Self$ Transparent = new(0f, 0f, 0f, 0f);
			/// <summary>
			/// The color (1, 1, 1, 1).
			/// </summary>
			public static readonly $Self$ White = new(1f, 1f, 1f, 1f);
			/// <summary>
			/// The color (0, 0, 0, 1).
			/// </summary>
			public static readonly $Self$ Black = new(0f, 0f, 0f, 1f);
		}
		""";
}
