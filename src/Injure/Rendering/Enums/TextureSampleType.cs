// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WGPUTextureSampleType))]
public readonly partial struct TextureSampleType {
	public enum Case {
		BindingNotUsed = 0,
		Undefined = 1,
		Float = 2,
		UnfilterableFloat = 3,
		Depth = 4,
		Sint = 5,
		Uint = 6,
	}
}
