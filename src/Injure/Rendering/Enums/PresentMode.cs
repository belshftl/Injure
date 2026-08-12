// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUPresentMode))]
public readonly partial struct PresentMode {
	public enum Case {
		Undefined = 0,
		Fifo = 1,
		FifoRelaxed = 2,
		Immediate = 3,
		Mailbox = 4,
	}
}
