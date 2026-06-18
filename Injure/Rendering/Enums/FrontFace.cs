// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUFrontFace))]
public readonly partial struct FrontFace {
	public enum Case {
		Undefined = 0,
		CCW = 1,
		CW = 2,
	}
}
