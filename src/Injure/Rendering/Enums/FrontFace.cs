// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUFrontFace))]
public readonly partial struct FrontFace {
	public enum Case {
		Undefined = 0,
		CCW = 1,
		CW = 2,
	}
}
