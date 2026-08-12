// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUTextureDimension))]
public readonly partial struct TextureDimension {
	public enum Case {
		Undefined = 0,
		Dimension1D = 1,
		Dimension2D = 2,
		Dimension3D = 3,
	}
}
