// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Injure.Game;

/// <summary>
/// What one drawable unit corresponds to on <see cref="StandardGame"/>'s primary target, i.e.
/// which <see cref="Draw.Viewport"/> it uses: logical units, physical pixels, or a fixed-size area
/// letterboxed into the window.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct PrimaryViewMode : IEquatable<PrimaryViewMode>, IHasInvalidDefault {
	internal enum Kind : byte {
		Invalid,
		LogicalUnits,
		PhysicalPixels,
		Letterbox,
	}

	internal Kind Mode { get; }
	internal Vector2 ContentSize { get; }
	internal bool IntegerScale { get; }

	private PrimaryViewMode(Kind mode, Vector2 contentSize, bool integerScale) {
		Mode = mode;
		ContentSize = contentSize;
		IntegerScale = integerScale;
	}

	/// <summary>
	/// The drawable area covers the whole window, and one drawable unit has a side length of
	/// <see cref="Sdl.SdlWindowState.PixelDensity"/> target pixels. In other words, drawable units
	/// correspond to logical units, i.e. window coordinates: as such, the content is the same size on
	/// HiDPI and regular displays, which is why this is the default.
	/// </summary>
	/// <remarks>
	/// If there is no additional transform from e.g. <see cref="Draw.CanvasParams.Transform"/>,
	/// pointer positions coincide with draw coordinates.
	/// </remarks>
	public static readonly PrimaryViewMode LogicalUnits = new(Kind.LogicalUnits, default, false);

	/// <summary>
	/// The drawable area covers the whole window, and one drawable unit has a side length of one
	/// target pixel.
	/// </summary>
	/// <remarks>
	/// May be useful for e.g. games that present a low-resolution render target at an integer scale,
	/// which only stays sharp if the scale is computed in physical pixels. For a fractional scale,
	/// draw the render target with <see cref="Draw.TexFilter.AreaWeighted"/>, which works in either
	/// mode.
	/// </remarks>
	public static readonly PrimaryViewMode PhysicalPixels = new(Kind.PhysicalPixels, default, false);

	/// <summary>
	/// The drawable area is a fixed area of size <paramref name="contentSize"/>, letterboxed into the
	/// window: the viewport is <see cref="Draw.Viewport.Letterbox(Vector2, Vector2, bool)"/> of it into
	/// the window's size in physical pixels.
	/// </summary>
	/// <param name="contentSize">Size of the area, e.g. a fixed logical resolution.</param>
	/// <param name="integerScale">
	/// Whether to round the scale down to a whole number of physical pixels; see
	/// <see cref="Draw.Viewport.Letterbox(Vector2, Vector2, bool)"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if a component of <paramref name="contentSize"/> isn't positive and finite.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Technically not a strict letterbox, since it may pillarbox too. Injure uses "letterbox" to
	/// mean both, since the term "pillarbox" is more obscure and the distinction is irrelevant in
	/// this case.
	/// </para>
	/// <para>
	/// For games that want to draw everything, including UIs/etc, at a fixed logical resolution such
	/// as 1920x1080, rather than scaling drawn elements with window size. In order to mix that with
	/// the real window resolution, you can use another mode and instead render into a render target
	/// of the desired fixed resolution, drawn into the <see cref="Draw.Viewport.PixelRect"/> of
	/// <see cref="Draw.Viewport.Letterbox(Vector2, Vector2, bool)"/>; this should typically use
	/// <see cref="PhysicalPixels"/>.
	/// </para>
	/// </remarks>
	public static PrimaryViewMode Letterbox(Vector2 contentSize, bool integerScale = false) {
		if (!float.IsFinite(contentSize.X) || !float.IsFinite(contentSize.Y) || contentSize.X <= 0f || contentSize.Y <= 0f)
			throw new ArgumentOutOfRangeException(nameof(contentSize), contentSize, "size components must be positive and finite");
		return new PrimaryViewMode(Kind.Letterbox, contentSize, integerScale);
	}

	public bool Equals(PrimaryViewMode other) => Mode == other.Mode && ContentSize == other.ContentSize && IntegerScale == other.IntegerScale;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is PrimaryViewMode other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Mode, ContentSize, IntegerScale);
	public static bool operator ==(PrimaryViewMode left, PrimaryViewMode right) => left.Equals(right);
	public static bool operator !=(PrimaryViewMode left, PrimaryViewMode right) => !left.Equals(right);

	internal bool IsDefault => Mode == Kind.Invalid;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
