// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUStoreOp))]
public readonly partial struct StoreOp {
	public enum Case {
		Undefined = 0,
		Store = 1,
		Discard = 2,
	}
}
