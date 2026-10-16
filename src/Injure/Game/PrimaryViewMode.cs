// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Game;

/// <summary>
/// Whether <see cref="StandardGame"/>'s primary target's view matrix should factor in HiDPI pixel
/// density, i.e. whether draw coordinates on the window are logical units or physical pixels.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="LogicalUnits"/>.
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // the zero case is the recommended mode, not a none-like one
public readonly partial struct PrimaryViewMode {
	/// <summary>Raw switch tag for <see cref="PrimaryViewMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Draw coordinates are logical units, i.e. window coordinates: the view matrix scales by the
		/// window's <see cref="Sdl.SdlWindowState.PixelDensity"/>, so content is the same size on
		/// HiDPI and regular displays. If there is no additional transform from e.g.
		/// <see cref="Draw.CanvasParams.Transform"/>, pointer positions match draw coordinates.
		/// </summary>
		LogicalUnits,

		/// <summary>
		/// Draw coordinates are physical pixels: the view matrix is identity. For games that present a
		/// low-resolution render target at an integer scale, which only stays sharp when the scale
		/// is computed in physical pixels.
		/// </summary>
		PhysicalPixels,
	}
}
