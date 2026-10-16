// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Host;
using Injure.Primitives;

namespace Injure.Tests.Draw;

public sealed class WindowCoordsTests {
	[Fact]
	public void PixelDensityScalesToPixels() {
		Assert.Equal(new Vector2(20, 30), WindowCoords.ToPixels(new WindowPos(10, 15), 2f));
		Assert.Equal(new Vector2(15, 22.5f), WindowCoords.ToPixels(new WindowPos(10, 15), 1.5f));
	}

	[Fact]
	public void LetterboxedVirtualResolutionIsUndone() {
		// a 320x180 render target drawn at 4x into a 1280x800 target, centered vertically, on a 2x
		// density window (window size 640x400)
		RectF dest = new(0, 40, 1280, 720);
		Matrix3x2 view = Matrix3x2.Identity;
		Matrix3x2 m = Matrix3x2.CreateScale(dest.Width / 320f, dest.Height / 180f)
			* Matrix3x2.CreateTranslation(dest.X, dest.Y) * view;
		Assert.Equal(new Vector2(0, 0), WindowCoords.ToDraw(new WindowPos(0, 20), 2f, m));
		Assert.Equal(new Vector2(160, 90), WindowCoords.ToDraw(new WindowPos(320, 200), 2f, m));
		// in the top bar: above the render target
		Assert.True(WindowCoords.ToDraw(new WindowPos(0, 0), 2f, m).Y < 0);
	}

	[Fact]
	public void LogicalUnitsViewIsUndone() {
		var view = Matrix3x2.CreateScale(2f);
		// logical units match window coordinates when the view is the pixel density
		Assert.Equal(new Vector2(10, 15), WindowCoords.ToDraw(new WindowPos(10, 15), 2f, view));
	}

	[Fact]
	public void DeltasIgnoreTranslation() {
		Matrix3x2 m = Matrix3x2.CreateScale(4f) * Matrix3x2.CreateTranslation(100, 50);
		Assert.Equal(new Vector2(1, -0.5f), WindowCoords.ToDraw(new WindowDelta(2, -1), 2f, m));
	}

	[Fact]
	public void BadInputsAreRejected() {
		Assert.Throws<ArgumentOutOfRangeException>(() => WindowCoords.ToPixels(default(WindowPos), 0f));
		Assert.Throws<ArgumentOutOfRangeException>(() => WindowCoords.ToPixels(default(WindowPos), float.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(() => WindowCoords.ToPixels(default(WindowPos), float.PositiveInfinity));
		Assert.Throws<ArgumentOutOfRangeException>(() => WindowCoords.ToDraw(default(WindowPos), 1f, Matrix3x2.CreateScale(0f)));
		Assert.Throws<ArgumentOutOfRangeException>(() => WindowCoords.ToDraw(WindowDelta.Zero, 1f, Matrix3x2.CreateScale(1f, 0f)));
	}
}
