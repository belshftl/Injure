// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Primitives;

/// <summary>
/// 32bpp RGBA color with no color space information. Used for byte data where the meaning is
/// defined by the API that produced it, e.g. texture readback.
/// </summary>
[Color32Type]
public readonly partial struct RawColor32 {
	/// <summary>
	/// Reinterprets this value as if it is sRGB-encoded, without changing the bytes.
	/// </summary>
	public SrgbColor32 AssumeSrgb() => new(R, G, B, A);

	/// <summary>
	/// Normalizes this value to a <see cref="RawColorF128"/>, with each channel mapped from [0, 255]
	/// to [0, 1].
	/// </summary>
	public RawColorF128 ToRawF128() => new(R / 255f, G / 255f, B / 255f, A / 255f);
}
