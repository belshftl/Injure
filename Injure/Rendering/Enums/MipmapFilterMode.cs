// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUMipmapFilterMode))]
public readonly partial struct MipmapFilterMode {
	public enum Case {
		Undefined = 0,
		Nearest = 1,
		Linear = 2,
	}
}
