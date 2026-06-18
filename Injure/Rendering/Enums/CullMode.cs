// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUCullMode))]
public readonly partial struct CullMode {
	public enum Case {
		Undefined = 0,
		None = 1,
		Front = 2,
		Back = 3,
	}
}
