// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUStoreOp))]
public readonly partial struct StoreOp {
	public enum Case {
		Undefined = 0,
		Store = 1,
		Discard = 2,
	}
}
