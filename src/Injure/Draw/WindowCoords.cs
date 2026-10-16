// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Host;

namespace Injure.Draw;

/// <summary>
/// Maps window coordinates (<see cref="WindowPos"/> and <see cref="WindowDelta"/>), e.g. pointer
/// positions and movements from <see cref="HostPointerMoveEvent"/>, to target pixels and to draw
/// coordinates.
/// </summary>
/// <remarks>
/// <para>
/// Window coordinates are the unit the OS reports pointer positions in. One window coordinate
/// spans as many physical pixels as the window's pixel density, e.g. 2 on a typical macOS HiDPI
/// display and 1 on Windows (where window coordinates = pixels). The pixel density comes from the
/// window, e.g. <c>SDL_GetWindowPixelDensity</c>.
/// </para>
/// <para>
/// Mapping to target pixels assumes the primary target covers the whole window at the window's
/// size in pixels, which is the case for a window's own surface. An embedded surface covering only
/// part of a window needs its own offset applied first.
/// </para>
/// <para>
/// Mapping to draw coordinates inverts a matrix mapping draw coordinates to target pixels. For
/// drawing straight into the primary target, that's <see cref="Canvas.EffectiveTransform"/>, or
/// <see cref="CanvasTarget.View"/> of the primary target for coordinates before the transform. For
/// a fixed virtual resolution drawn into a <see cref="RenderTarget2d"/> and then drawn scaled
/// into the primary target, it's the matrix mapping render target pixels to where they land. For
/// a draw to <c>dest</c> under the primary target's view matrix <c>view</c> (with an identity
/// transform), that's:
/// <code>
/// Matrix3x2.CreateScale(dst.Width / rt.Width, dst.Height / rt.Height)
/// 	* Matrix3x2.CreateTranslation(dst.X, dst.Y) * view
/// </code>
/// </para>
/// </remarks>
public static class WindowCoords {
	/// <summary>
	/// Maps a position in window coordinates to target pixels.
	/// </summary>
	/// <param name="pos">Position in window coordinates.</param>
	/// <param name="pixelDensity">The window's pixels per window coordinate.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="pixelDensity"/> isn't finite and positive.
	/// </exception>
	public static Vector2 ToPixels(WindowPos pos, float pixelDensity) {
		chkDensity(pixelDensity);
		return pos.ToRaw() * pixelDensity;
	}

	/// <summary>
	/// Maps a movement in window coordinates to target pixels.
	/// </summary>
	/// <param name="delta">Movement in window coordinates.</param>
	/// <param name="pixelDensity">The window's pixels per window coordinate.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="pixelDensity"/> isn't finite and positive.
	/// </exception>
	public static Vector2 ToPixels(WindowDelta delta, float pixelDensity) {
		chkDensity(pixelDensity);
		return delta.ToRaw() * pixelDensity;
	}

	/// <summary>
	/// Maps a position in window coordinates to draw coordinates, by inverting
	/// <paramref name="drawToPixels"/>.
	/// </summary>
	/// <param name="pos">Position in window coordinates.</param>
	/// <param name="pixelDensity">The window's pixels per window coordinate.</param>
	/// <param name="drawToPixels">
	/// Matrix mapping draw coordinates to target pixels; must be invertible (see
	/// <see cref="MatrixInvert"/>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="pixelDensity"/> isn't finite and positive, or
	/// <paramref name="drawToPixels"/> isn't invertible.
	/// </exception>
	/// <remarks>
	/// The result can lie outside the drawn area, e.g. in the letterbox bars; check it against the
	/// virtual resolution if that matters.
	/// </remarks>
	public static Vector2 ToDraw(WindowPos pos, float pixelDensity, Matrix3x2 drawToPixels) =>
		Vector2.Transform(ToPixels(pos, pixelDensity), invert(drawToPixels, nameof(drawToPixels)));

	/// <summary>
	/// Maps a movement in window coordinates to draw coordinates, by inverting
	/// <paramref name="drawToPixels"/> without its translation.
	/// </summary>
	/// <param name="delta">Movement in window coordinates.</param>
	/// <param name="pixelDensity">The window's pixels per window coordinate.</param>
	/// <param name="drawToPixels">
	/// Matrix mapping draw coordinates to target pixels; must be invertible (see
	/// <see cref="MatrixInvert"/>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="pixelDensity"/> isn't finite and positive, or
	/// <paramref name="drawToPixels"/> isn't invertible.
	/// </exception>
	public static Vector2 ToDraw(WindowDelta delta, float pixelDensity, Matrix3x2 drawToPixels) =>
		Vector2.TransformNormal(ToPixels(delta, pixelDensity), invert(drawToPixels, nameof(drawToPixels)));

	private static void chkDensity(float pixelDensity) {
		// written so that NaN fails too
		if (!(pixelDensity > 0f) || !float.IsFinite(pixelDensity))
			throw new ArgumentOutOfRangeException(nameof(pixelDensity), pixelDensity, "pixel density must be finite and positive");
	}

	private static Matrix3x2 invert(Matrix3x2 m, string paramName) {
		if (!MatrixInvert.TryInvert(m, out Matrix3x2 inv))
			throw new ArgumentOutOfRangeException(paramName, m, "matrix must be invertible");
		return inv;
	}
}
