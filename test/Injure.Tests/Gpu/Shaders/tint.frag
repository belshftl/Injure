// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

// GLSL equivalent of GpuRig.Wgsl's fs, compiled to SPIR-V by compile.sh
#version 450

layout(set = 0, binding = 0) uniform Tint {
	vec4 color;
} tint;

layout(location = 0) out vec4 o;

void main() {
	o = tint.color;
}
