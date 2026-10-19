// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.Assets;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// A rectangle of a texture that <see cref="Canvas"/> draws as if it were a texture of its own,
/// e.g. a tile of a tileset or a frame of an animation in an atlas.
/// </summary>
/// <remarks>
/// <para>
/// Implicitly casts to <see cref="DrawableTexture"/>, so 
/// <see cref="Canvas.Texture(DrawableTexture, in TexDraw)"/> draws it like any texture. The natural
/// size (see <see cref="TexDraw"/>) is the slice's size, and <see cref="TexDraw.Source"/> is
/// relative to the slice; for example, <see cref="TexRegion.Whole"/> is the whole slice. A
/// <see cref="TexRegion"/> reaching outside the slice is rejected when drawing, since it would
/// sample neighboring texels of the underlying texture.
/// </para>
/// <para>
/// Slices of the same texture draw without starting a new draw call, which separate textures can't,
/// which makes atlases faster than separate textures for many small sprites. With
/// <see cref="TexFilter.Linear"/> or <see cref="TexFilter.AreaWeighted"/>, samples near a slice's
/// edge blend with the neighboring texels, so atlases drawn that way need padding between slices;
/// see <see cref="TexSampling"/>.
/// </para>
/// <para>
/// A slice only refers to its texture; it doesn't keep it alive or track its lifetime. To follow an
/// asset's hot reloads, slice the <see cref="Asset{T}"/> rather than a <see cref="Texture2d"/>
/// borrowed from it.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct Texture2dSlice : IEquatable<Texture2dSlice>, IHasInvalidDefault {
	/// <summary>
	/// The whole texture this slice is part of.
	/// </summary>
	public DrawableTexture Texture { get; }

	/// <summary>
	/// The rectangle of <see cref="Texture"/> this slice covers, in texels.
	/// </summary>
	public RectI Rect { get; }

	/// <summary>
	/// Width of the slice in texels.
	/// </summary>
	public int Width => Rect.Width;

	/// <summary>
	/// Height of the slice in texels.
	/// </summary>
	public int Height => Rect.Height;

	/// <summary>
	/// Creates a slice covering <paramref name="rect"/> of <paramref name="texture"/>.
	/// </summary>
	/// <param name="texture">
	/// Texture to slice. If it's a slice itself, <paramref name="rect"/> is relative to it, as with
	/// <see cref="Slice(RectI)"/>.
	/// </param>
	/// <param name="rect">Rectangle of <paramref name="texture"/> in texels.</param>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="texture"/> is the <see langword="default"/> value.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="rect"/> has a negative position or a non-positive size, or if
	/// <paramref name="rect"/> doesn't fit in <paramref name="texture"/>. Fitting is checked here for
	/// slices, <see cref="Texture2d"/>s, and <see cref="RenderTarget2d"/>s, and on draw for assets,
	/// since the size of an asset isn't known without borrowing it.
	/// </exception>
	public Texture2dSlice(DrawableTexture texture, RectI rect) {
		if (texture.IsDefault)
			throw ArgumentInvalidDefaultException.For<DrawableTexture>(nameof(texture));
		if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0)
			throw new ArgumentException($"slice rect must have a non-negative position and a positive size, got {rect}", nameof(rect));
		if (texture.SliceRect is RectI outer) {
			if (!fits(rect, outer.Width, outer.Height))
				throw new ArgumentException($"slice rect {rect} doesn't fit in the {outer.Width}x{outer.Height} slice it's relative to", nameof(rect));
			rect = new RectI(outer.X + rect.X, outer.Y + rect.Y, rect.Width, rect.Height);
		} else if (texture.TryGetWholeSize(out uint w, out uint h) && !fits(rect, w, h)) {
			throw new ArgumentException($"slice rect {rect} doesn't fit in the {w}x{h} texture", nameof(rect));
		}
		Texture = texture.Whole;
		Rect = rect;
	}

	/// <summary>
	/// Creates a slice of this slice.
	/// </summary>
	/// <param name="rect">Rectangle relative to this slice's top-left corner, in texels.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="rect"/> has a negative position or a non-positive size, or doesn't
	/// fit in this slice.
	/// </exception>
	public Texture2dSlice Slice(RectI rect) =>
		!IsDefault ? new(this, rect) : throw InvalidDefaultException.For<Texture2dSlice>();

	/// <summary>
	/// Slices <paramref name="texture"/> into a grid of equally sized cells, e.g. a tileset or a
	/// spritesheet. Cells are ordered row by row, left to right within a row.
	/// </summary>
	/// <param name="texture">
	/// Texture to slice; if it's a slice itself, the grid is relative to it.
	/// </param>
	/// <param name="cellSize">Size of each cell in texels.</param>
	/// <param name="columns">Number of cells per row.</param>
	/// <param name="rows">Number of rows.</param>
	/// <param name="margin">Texels before the first row and column.</param>
	/// <param name="spacing">Texels between neighboring rows and columns.</param>
	/// <returns>
	/// The cells; cell (<c>x</c>, <c>y</c>) is at index <c>y * <paramref name="columns"/> + x</c>.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="cellSize"/>, <paramref name="columns"/>, or <paramref name="rows"/>
	/// isn't positive, or <paramref name="margin"/> or <paramref name="spacing"/> is negative.
	/// </exception>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="texture"/> is the <see langword="default"/> value.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the grid doesn't fit in <paramref name="texture"/>, as checked by
	/// <see cref="Texture2dSlice(DrawableTexture, RectI)"/>.
	/// </exception>
	public static Texture2dSlice[] Grid(DrawableTexture texture, Vector2Int cellSize, int columns, int rows, int margin = 0, int spacing = 0) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize.X, nameof(cellSize));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize.Y, nameof(cellSize));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
		ArgumentOutOfRangeException.ThrowIfNegative(margin);
		ArgumentOutOfRangeException.ThrowIfNegative(spacing);
		if (texture.IsDefault)
			throw ArgumentInvalidDefaultException.For<DrawableTexture>(nameof(texture));
		// check the bottom right cell first to report a non-fitting grid before allocating
		_ = new Texture2dSlice(texture, cell(columns - 1, rows - 1));
		var cells = new Texture2dSlice[checked(columns * rows)];
		for (int y = 0; y < rows; y++)
			for (int x = 0; x < columns; x++)
				cells[y * columns + x] = new Texture2dSlice(texture, cell(x, y));
		return cells;

		RectI cell(int x, int y) => new(
			checked(margin + x * (cellSize.X + spacing)),
			checked(margin + y * (cellSize.Y + spacing)),
			cellSize.X,
			cellSize.Y
		);
	}

	/// <summary>
	/// Slices <paramref name="texture"/> into as many equally sized cells as fit, with
	/// <paramref name="margin"/> on all sides; see
	/// <see cref="Grid(DrawableTexture, Vector2Int, int, int, int, int)"/>.
	/// </summary>
	/// <param name="texture">Texture to slice.</param>
	/// <param name="cellSize">Size of each cell in texels.</param>
	/// <param name="margin">Texels between the texture's edges and the outermost cells.</param>
	/// <param name="spacing">Texels between neighboring rows and columns.</param>
	/// <returns>The cells, ordered row by row.</returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="texture"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="cellSize"/> isn't positive, or <paramref name="margin"/> or
	/// <paramref name="spacing"/> is negative.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if not even one cell fits.
	/// </exception>
	public static Texture2dSlice[] Grid(Texture2d texture, Vector2Int cellSize, int margin = 0, int spacing = 0) {
		ArgumentNullException.ThrowIfNull(texture);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize.X, nameof(cellSize));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize.Y, nameof(cellSize));
		ArgumentOutOfRangeException.ThrowIfNegative(margin);
		ArgumentOutOfRangeException.ThrowIfNegative(spacing);
		// n cells take n * cell + (n - 1) * spacing texels
		long columns = (texture.Width - 2L * margin + spacing) / (cellSize.X + (long)spacing);
		long rows = (texture.Height - 2L * margin + spacing) / (cellSize.Y + (long)spacing);
		if (columns <= 0 || rows <= 0)
			throw new ArgumentException($"no {cellSize.X}x{cellSize.Y} cell fits in the {texture.Width}x{texture.Height} texture with margin {margin}", nameof(cellSize));
		return Grid(texture, cellSize, (int)columns, (int)rows, margin, spacing);
	}

	private static bool fits(RectI rect, long width, long height) =>
		(long)rect.X + rect.Width <= width && (long)rect.Y + rect.Height <= height;

	public bool Equals(Texture2dSlice other) => Texture == other.Texture && Rect == other.Rect;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is Texture2dSlice other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Texture, Rect);
	public static bool operator ==(Texture2dSlice left, Texture2dSlice right) => left.Equals(right);
	public static bool operator !=(Texture2dSlice left, Texture2dSlice right) => !left.Equals(right);

	internal bool IsDefault => Texture.IsDefault;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
