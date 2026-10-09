// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Primitives;

/// <summary>
/// 32bpp RGBA color with sRGB-encoded <see cref="R"/>/<see cref="G"/>/<see cref="B"/> and linear
/// <see cref="A"/>. This is what color pickers and hex codes like <c>#CBA6F7</c> produce.
/// </summary>
[Color32Type]
public readonly partial struct SrgbColor32 {
	/// <summary>
	/// Drops the color space information, without changing the bytes.
	/// </summary>
	public RawColor32 ToRaw() => new(R, G, B, A);

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
