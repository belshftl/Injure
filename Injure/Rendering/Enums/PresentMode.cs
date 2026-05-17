// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUPresentMode))]
public readonly partial struct PresentMode {
	public enum Case {
		Undefined = 0,
		Fifo = 1,
		FifoRelaxed = 2,
		Immediate = 3,
		Mailbox = 4,
	}
}
