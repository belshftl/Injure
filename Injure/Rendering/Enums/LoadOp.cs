// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPULoadOp))]
public readonly partial struct LoadOp {
	public enum Case {
		Undefined = 0,
		Load = 1,
		Clear = 2,
	}
}
