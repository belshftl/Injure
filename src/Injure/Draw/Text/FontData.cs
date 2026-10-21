// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using Injure.Assets;

namespace Injure.Draw.Text;

/// <summary>
/// A loaded font file, which may contain several faces. Obtained from
/// <see cref="TextSystem.LoadFont(byte[], string?)"/> or as an asset.
/// </summary>
/// <remarks>
/// Only OpenType font files, i.e. fonts in the sfnt container, are supported as valid font data:
/// that's a TrueType (<c>.ttf</c>) or OpenType (<c>.otf</c>) font with TrueType/CFF/CFF2 outlines,
/// or a collection of such fonts (<c>.ttc</c>, <c>.otc</c>). Variable fonts are used at their
/// default instance. Other formats, including WOFF and WOFF2, are unsupported.
/// </remarks>
public sealed class FontData {
	private static ulong nextId = 0;

	internal ulong Id { get; }
	internal byte[] DataArr { get; }

	/// <summary>
	/// The font data.
	/// </summary>
	public ReadOnlySpan<byte> Data => DataArr;

	/// <summary>
	/// Name used in error messages, e.g. the asset ID or file name.
	/// </summary>
	public string? DebugName { get; }

	/// <summary>
	/// Number of faces in the file; 1 unless it's a font collection.
	/// </summary>
	public int FaceCount { get; }

	/// <remarks>
	/// <paramref name="data"/> is directly stored into <see cref="DataArr"/> rather than copied out.
	/// </remarks>
	internal FontData(byte[] data, string? debugName, int faceCount) {
		InternalStateException.ThrowIfNull(data);
		Id = Interlocked.Increment(ref nextId);
		DataArr = data;
		DebugName = debugName;
		FaceCount = faceCount;
	}

	internal static void ThrowIfNoOpentypeHeader(byte[] data, string? debugName) {
		if (!HasOpentypeHeader(data))
			throw new ArgumentException($"{debugName ?? "the data"} isn't an OpenType font or collection", nameof(data));
	}

	internal static bool HasOpentypeHeader(ReadOnlySpan<byte> data) =>
		data.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(data) is
			0x00010000 or // truetype, or opentype with truetype outlines
			0x4f54544f or // 'OTTO' (opentype with cff/cff2 outlines)
			0x74727565 or // 'true' (apple's tag for truetype)
			0x74746366;   // 'ttcf' (truetype collection)
}

internal enum FontSourceKind {
	Direct,
	Asset,
}

/// <summary>
/// A font face to draw text with: a <see cref="FontData"/> or font asset, the index of the face within
/// it, and how to rasterize the face's glyphs.
/// </summary>
/// <remarks>
/// <para>
/// Two specs are equal if they refer to the same <see cref="FontData"/> object or the same asset, the
/// same face, and equal raster options.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct FontSpec : IHasInvalidDefault, IEquatable<FontSpec> {
	private readonly FontData? direct;
	private readonly Asset<FontData>? asset;

	/// <summary>
	/// Index of the face within the font file.
	/// </summary>
	public int FaceIndex { get; }

	/// <summary>
	/// How the face's glyphs are rasterized.
	/// </summary>
	public FontRasterOptions Raster { get; init; }

	internal FontSourceKind SourceKind { get; }
	internal FontData Direct => SourceKind == FontSourceKind.Direct
		? direct!
		: throw new InternalStateException("this FontSpec is not sourced from a FontData");
	internal Asset<FontData> Asset => SourceKind == FontSourceKind.Asset
		? asset!
		: throw new InternalStateException("this FontSpec is not sourced from an Asset<FontData>");

	/// <summary>
	/// Creates a spec for a face of a loaded font.
	/// </summary>
	/// <param name="font">The font.</param>
	/// <param name="faceIndex">Index of the face within the font file.</param>
	/// <param name="raster">
	/// How to rasterize the face's glyphs, or <see langword="null"/> for
	/// <c>new FontRasterOptions()</c>.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="font"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="faceIndex"/> is negative.
	/// </exception>
	/// <remarks>
	/// A <paramref name="faceIndex"/> past the font's last face is reported once the face is used.
	/// </remarks>
	public FontSpec(FontData font, int faceIndex = 0, FontRasterOptions? raster = null) {
		ArgumentNullException.ThrowIfNull(font);
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		direct = font;
		SourceKind = FontSourceKind.Direct;
		FaceIndex = faceIndex;
		Raster = raster ?? new FontRasterOptions();
	}

	/// <summary>
	/// Creates a spec for a face of a font asset, which follows the asset across reloads.
	/// </summary>
	/// <inheritdoc cref="FontSpec(FontData, int, FontRasterOptions?)"/>
	public FontSpec(Asset<FontData> font, int faceIndex = 0, FontRasterOptions? raster = null) {
		ArgumentNullException.ThrowIfNull(font);
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		asset = font;
		SourceKind = FontSourceKind.Asset;
		FaceIndex = faceIndex;
		Raster = raster ?? new FontRasterOptions();
	}

	/// <summary>
	/// Creates a spec for the first face of <paramref name="font"/>.
	/// </summary>
	public static implicit operator FontSpec(FontData font) => new(font);

	/// <summary>
	/// Creates a spec for the first face of <paramref name="font"/>.
	/// </summary>
	public static implicit operator FontSpec(Asset<FontData> font) => new(font);

	/// <summary>
	/// The source's identity: the font's ID, or the asset's slot ID, which every handle to the asset
	/// shares.
	/// </summary>
	private ulong sourceId => SourceKind == FontSourceKind.Direct ? direct!.Id : asset!.SlotId;

	public bool Equals(FontSpec other) {
		if (direct is null && asset is null)
			return other.direct is null && other.asset is null;
		return other.SourceKind == SourceKind
			&& (other.direct is not null || other.asset is not null)
			&& other.sourceId == sourceId
			&& other.FaceIndex == FaceIndex
			&& other.Raster == Raster;
	}
	public override bool Equals(object? obj) => obj is FontSpec other && Equals(other);
	public override int GetHashCode() =>
		direct is null && asset is null ? 0 : HashCode.Combine(SourceKind, sourceId, FaceIndex, Raster);
	public static bool operator ==(FontSpec left, FontSpec right) => left.Equals(right);
	public static bool operator !=(FontSpec left, FontSpec right) => !left.Equals(right);

	bool IHasInvalidDefault.IsDefault => direct is null && asset is null;
}

/// <summary>
/// Shorthands for creating <see cref="FontSpec"/>s.
/// </summary>
public static class FontSpecExtensions {
	/// <summary>
	/// Creates a spec for the face at <paramref name="faceIndex"/> of <paramref name="font"/>.
	/// </summary>
	public static FontSpec Face(this FontData font, int faceIndex) => new(font, faceIndex);

	/// <summary>
	/// Creates a spec for the face at <paramref name="faceIndex"/> of <paramref name="font"/>.
	/// </summary>
	public static FontSpec Face(this Asset<FontData> font, int faceIndex) => new(font, faceIndex);
}
