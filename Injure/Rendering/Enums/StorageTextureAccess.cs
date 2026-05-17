// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WebGPU.WGPUStorageTextureAccess))]
public readonly partial struct StorageTextureAccess {
	public enum Case {
		BindingNotUsed = 0,
		Undefined = 1,
		WriteOnly = 2,
		ReadOnly = 3,
		ReadWrite = 4,
	}
}
