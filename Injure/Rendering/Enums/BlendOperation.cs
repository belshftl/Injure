// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUBlendOperation))]
public readonly partial struct BlendOperation {
	public enum Case {
		Undefined = 0,
		Add = 1,
		Subtract = 2,
		ReverseSubtract = 3,
		Min = 4,
		Max = 5,
	}
}
