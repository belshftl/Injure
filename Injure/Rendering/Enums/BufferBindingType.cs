// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WGPUBufferBindingType))]
public readonly partial struct BufferBindingType {
	public enum Case {
		BindingNotUsed = 0,
		Undefined = 1,
		Uniform = 2,
		Storage = 3,
		ReadOnlyStorage = 4,
	}
}
