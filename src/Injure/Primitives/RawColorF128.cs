// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Primitives;

/// <summary>
/// Single-precision (f32) float RGBA color with no color space information.
/// </summary>
[ColorF128Type]
public readonly partial struct RawColorF128 {
	internal WebGPU.WGPUColor ToWebgpuColor() => new(R, G, B, A);
}
