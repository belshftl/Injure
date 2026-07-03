// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPULoadOp))]
public readonly partial struct LoadOp {
	public enum Case {
		Undefined = 0,
		Load = 1,
		Clear = 2,
	}
}
