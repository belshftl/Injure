#!/bin/sh
# SPDX-FileCopyrightText: 2026 belshftl
# SPDX-License-Identifier: MIT

# regenerates the .spv fixtures from their GLSL sources; needs glslc (from shaderc). the .spv files
# are committed, so running the tests doesn't need glslc
set -eu
cd "$(dirname "$0")"
for src in *.vert *.frag; do
	glslc --target-env=vulkan1.0 -O0 "$src" -o "$src.spv"
done
