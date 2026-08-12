// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedFlags]
[ClosedFlagsMirror(typeof(WGPUShaderStage))]
public readonly partial struct ShaderStage {
	[Flags]
	public enum Bits : ulong {
		None = 0ul,
		Vertex = 1ul,
		Fragment = 2ul,
		Compute = 4ul,
	}
}
