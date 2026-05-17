// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUPowerPreference))]
public readonly partial struct PowerPreference {
	public enum Case {
		Undefined = 0,
		LowPower = 1,
		HighPerformance = 2,
	}
}
