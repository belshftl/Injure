// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUBackendType))]
public readonly partial struct BackendType {
	public enum Case {
		Undefined = 0,
		Null = 1,
		WebGPU = 2,
		D3D11 = 3,
		D3D12 = 4,
		Metal = 5,
		Vulkan = 6,
		OpenGL = 7,
		OpenGLES = 8,
	}
}
