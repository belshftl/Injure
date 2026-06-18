// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedFlags]
[ClosedFlagsMirror(typeof(WebGPU.WGPUShaderStage))]
public readonly partial struct ShaderStage {
	[Flags]
	public enum Bits : ulong {
		None = 0ul,
		Vertex = 1ul,
		Fragment = 2ul,
		Compute = 4ul,
	}
}
