// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WGPUSamplerBindingType))]
public readonly partial struct SamplerBindingType {
	public enum Case {
		BindingNotUsed = 0,
		Undefined = 1,
		Filtering = 2,
		NonFiltering = 3,
		Comparison = 4,
	}
}
