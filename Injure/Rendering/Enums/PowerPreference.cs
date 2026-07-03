// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUPowerPreference))]
public readonly partial struct PowerPreference {
	public enum Case {
		Undefined = 0,
		LowPower = 1,
		HighPerformance = 2,
	}
}
