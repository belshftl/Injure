// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUAddressMode))]
public readonly partial struct AddressMode {
	public enum Case {
		Undefined = 0,
		ClampToEdge = 1,
		Repeat = 2,
		MirrorRepeat = 3,
	}
}
