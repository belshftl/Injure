// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUAddressMode))]
public readonly partial struct AddressMode {
	public enum Case {
		Undefined = 0,
		ClampToEdge = 1,
		Repeat = 2,
		MirrorRepeat = 3,
	}
}
