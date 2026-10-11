// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Injure.Draw;

internal static class MatrixUtil {
	// @formatter:off
	/// <summary>
	/// Maps pixel coordinates of a <paramref name="w"/> by <paramref name="h"/> target, with the
	/// origin at the top left and +Y pointing down, to clip space.
	/// </summary>
	public static Matrix4x4 OrthoTopLeft(float w, float h) => new(
		2f / w, 0f,     0f, 0f,
		0f,    -2f / h, 0f, 0f,
		0f,     0f,     1f, 0f,
		-1f,    1f,     0f, 1f
	);
	// @formatter:on
}
