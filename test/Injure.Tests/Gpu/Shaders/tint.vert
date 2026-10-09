// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

// GLSL equivalent of GpuRig.Wgsl's vs, compiled to SPIR-V by compile.sh
#version 450

layout(location = 0) in vec3 p;

void main() {
	gl_Position = vec4(p, 1.0);
}
