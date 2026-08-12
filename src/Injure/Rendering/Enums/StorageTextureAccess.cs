// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WGPUStorageTextureAccess))]
public readonly partial struct StorageTextureAccess {
	public enum Case {
		BindingNotUsed = 0,
		Undefined = 1,
		WriteOnly = 2,
		ReadOnly = 3,
		ReadWrite = 4,
	}
}
