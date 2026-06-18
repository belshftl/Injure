// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUFilterMode))]
public readonly partial struct FilterMode {
	public enum Case {
		Undefined = 0,
		Nearest = 1,
		Linear = 2,
	}
}
