// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUIndexFormat))]
public readonly partial struct IndexFormat {
	public enum Case {
		Undefined = 0,
		Uint16 = 1,
		Uint32 = 2,
	}
}
