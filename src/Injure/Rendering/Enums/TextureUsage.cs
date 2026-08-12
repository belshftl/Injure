// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedFlags]
[ClosedFlagsMirror(typeof(WGPUTextureUsage))]
public readonly partial struct TextureUsage {
	[Flags]
	public enum Bits : ulong {
		None = 0ul,
		CopySrc = 1ul,
		CopyDst = 2ul,
		TextureBinding = 4ul,
		StorageBinding = 8ul,
		RenderAttachment = 0x10ul,
	}
}
