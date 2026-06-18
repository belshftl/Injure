// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUTextureAspect))]
public readonly partial struct TextureAspect {
	public enum Case {
		Undefined = 0,
		All = 1,
		StencilOnly = 2,
		DepthOnly = 3,
	}
}
