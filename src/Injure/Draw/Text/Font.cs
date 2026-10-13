// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Draw.Text;

public sealed class Font {
	private static ulong nextId = 0;

	internal ulong Id { get; }
	internal byte[] Data { get; }
	public string? DebugName { get; }
	public int FaceCount { get; }

	// copies the data, since loaded faces reference it directly
	public Font(byte[] data, string? debugName, int faceCount) {
		ArgumentNullException.ThrowIfNull(data);
		Id = Interlocked.Increment(ref nextId);
		Data = data.ToArray();
		DebugName = debugName;
		FaceCount = faceCount;
	}
}

internal enum FontSourceKind {
	Direct,
	Asset,
}

public readonly struct FontSpec {
	private readonly Font? direct;
	private readonly Asset<Font>? asset;

	public int FaceIndex { get; }

	internal FontSourceKind SourceKind { get; }
	internal Font Direct => SourceKind == FontSourceKind.Direct
		? direct!
		: throw new InvalidOperationException("this FontSpec is not sourced from a Font");
	internal Asset<Font> Asset => SourceKind == FontSourceKind.Asset
		? asset!
		: throw new InvalidOperationException("this FontSpec is not sourced from an Asset<Font>");

	public FontSpec(Font font, int faceIndex = 0) {
		ArgumentNullException.ThrowIfNull(font);
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		direct = font;
		SourceKind = FontSourceKind.Direct;
		FaceIndex = faceIndex;
	}

	public FontSpec(Asset<Font> font, int faceIndex = 0) {
		ArgumentNullException.ThrowIfNull(font);
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		asset = font;
		SourceKind = FontSourceKind.Asset;
		FaceIndex = faceIndex;
	}

	public static implicit operator FontSpec(Font font) => new(font);
	public static implicit operator FontSpec(Asset<Font> font) => new(font);
}

public static class FontSpecExtensions {
	public static FontSpec Face(this Font font, int faceIndex) => new(font, faceIndex);
	public static FontSpec Face(this Asset<Font> font, int faceIndex) => new(font, faceIndex);
}
