// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WGPUVertexStepMode))]
public readonly partial struct VertexStepMode {
	public enum Case {
		VertexBufferNotUsed = 0,
		Undefined = 1,
		Vertex = 2,
		Instance = 3,
	}
}
