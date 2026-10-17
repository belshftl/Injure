// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Host;
using Injure.Primitives;

namespace Injure.Tests.Draw;

public static class ViewportTests {
	private static readonly Vector2 content320 = new(320, 180);

	[Fact]
	public static void WholeViewportsDivideByThePixelsPerUnit() {
		var vp = Viewport.Whole(new Vector2(300, 200), 1.5f);
		Assert.Equal(new Vector2(200, 400 / 3f), vp.Size);
		Assert.Equal(Vector2.Zero, vp.PixelOffset);
		Assert.Equal(new RectF(0, 0, 300, 200), vp.PixelRect);
		// zero-sized targets (e.g. minimized windows) are allowed
		Assert.Equal(Vector2.Zero, Viewport.Whole(Vector2.Zero, 2f).Size);
	}

	[Fact]
	public static void MappingRoundtrips() {
		Viewport vp = new(new Vector2(10, 20), 3f, new Vector2(50, 50));
		Assert.Equal(new Vector2(16, 26), vp.ToPixels(new Vector2(2, 2)));
		Assert.Equal(new Vector2(2, 2), vp.FromPixels(new Vector2(16, 26)));
		Assert.Equal(new Vector2(16, 26), Vector2.Transform(new Vector2(2, 2), vp.ToPixelsMatrix));
		Assert.Equal(new RectF(13, 23, 6, 9), vp.ToPixels(new RectF(1, 1, 2, 3)));
	}

	[Fact]
	public static void LetterboxPillarboxesWiderTargets() {
		var vp = Viewport.Letterbox(content320, new Vector2(1000, 360), false);
		// scale 2 by height, 640 wide, centered in 1000
		Assert.Equal(new RectF(180, 0, 640, 360), vp.PixelRect);
		Assert.Equal(2f, vp.PixelsPerUnit);
		Assert.Equal(content320, vp.Size);
	}

	[Fact]
	public static void LetterboxBarsTallerTargets() {
		Assert.Equal(new RectF(0, 70, 640, 360), Viewport.Letterbox(content320, new Vector2(640, 500), false).PixelRect);
	}

	[Fact]
	public static void LetterboxFractionalScalesFill() {
		Assert.Equal(new RectF(0, 0, 800, 450), Viewport.Letterbox(content320, new Vector2(800, 450), false).PixelRect);
	}

	[Fact]
	public static void LetterboxIntegerScalesRoundDownAndCenterOnWholePixels() {
		var vp = Viewport.Letterbox(content320, new Vector2(801, 451), true);
		// scale 2.5 rounds to 2: 640x360 in 801x451, offsets 80.5 and 45.5 round down
		Assert.Equal(new RectF(80, 45, 640, 360), vp.PixelRect);
		Assert.Equal(2f, vp.PixelsPerUnit);
	}

	[Fact]
	public static void LetterboxIntegerScalesBelowOneStayFractional() {
		Assert.Equal(new RectF(0, 0, 160, 90), Viewport.Letterbox(content320, new Vector2(160, 90), true).PixelRect);
	}

	// content larger than the target in both directions, with a different aspect ratio
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public static void LetterboxShrinksLargerContent(bool integerScale) {
		Vector2 big = new(1000, 500);
		// scale 0.3 by width: 300x150, centered vertically in 400
		Assert.Equal(new RectF(0, 125, 300, 150), Viewport.Letterbox(big, new Vector2(300, 400), integerScale).PixelRect);
		// scale 0.6 by height: 600x300, centered horizontally in 900
		RectF r = Viewport.Letterbox(big, new Vector2(900, 300), integerScale).PixelRect;
		Assert.Equal(600f, r.Width, 1e-3f);
		Assert.Equal(new Vector2(150, 0), r.Position);
	}

	// 324 * (1080 / 243) comes out a hair over 1440, so the offset comes out a hair under 240
	[Fact]
	public static void LetterboxOffsetsSurviveFloatError() {
		Assert.Equal(new Vector2(240, 0), Viewport.Letterbox(new Vector2(324, 243), new Vector2(1920, 1080), false).PixelOffset);
	}

	[Fact]
	public static void LetterboxedPointersMapIntoTheContent() {
		// 320x180 at 4x into a 1280x800 target, centered vertically, on a 2x density window (window
		// size 640x400)
		var vp = Viewport.Letterbox(content320, new Vector2(1280, 800), true);
		Assert.Equal(new RectF(0, 40, 1280, 720), vp.PixelRect);
		Assert.Equal(new Vector2(0, 0), vp.FromWindow(new WindowPos(0, 20), 2f));
		Assert.Equal(new Vector2(160, 90), vp.FromWindow(new WindowPos(320, 200), 2f));
		// in the top bar: above the drawable area
		Assert.True(vp.FromWindow(new WindowPos(0, 0), 2f).Y < 0);
	}

	[Fact]
	public static void LogicalUnitsMatchWindowCoordinates() {
		// logical units match window coordinates when the scale is the pixel density
		var vp = Viewport.Whole(new Vector2(200, 200), 2f);
		Assert.Equal(new Vector2(10, 15), vp.FromWindow(new WindowPos(10, 15), 2f));
		Assert.Equal(new Vector2(15, 22.5f), Viewport.Whole(new Vector2(200, 200)).FromWindow(new WindowPos(10, 15), 1.5f));
	}

	[Fact]
	public static void DeltasIgnoreTheOffset() {
		Viewport vp = new(new Vector2(100, 50), 4f, new Vector2(10, 10));
		Assert.Equal(new Vector2(1, -0.5f), vp.FromWindow(new WindowDelta(2, -1), 2f));
	}

	[Theory]
	[InlineData(0f)]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	public static void BadPixelDensitiesAreRejected(float density) {
		var vp = Viewport.Whole(new Vector2(4, 4));
		ArgumentOutOfRangeException a = Assert.Throws<ArgumentOutOfRangeException>(() => vp.FromWindow(default(WindowPos), density));
		Assert.Equal("pixelDensity", a.ParamName);
		Assert.Throws<ArgumentOutOfRangeException>(() => vp.FromWindow(WindowDelta.Zero, density));
	}

	[Fact]
	public static void InvalidViewportsAreRejected() {
		Vector2 one = Vector2.One;
		Assert.Equal("pixelOffset", Assert.Throws<ArgumentOutOfRangeException>(() => new Viewport(new Vector2(float.NaN, 0), 1f, one)).ParamName);
		foreach (float ppu in (float[])[0f, -1f, float.NaN, float.PositiveInfinity])
			Assert.Equal("pixelsPerUnit", Assert.Throws<ArgumentOutOfRangeException>(() => new Viewport(Vector2.Zero, ppu, one)).ParamName);
		Assert.Equal("size", Assert.Throws<ArgumentOutOfRangeException>(() => new Viewport(Vector2.Zero, 1f, new Vector2(-1, 1))).ParamName);
		// the size in pixels overflows
		Assert.Throws<ArgumentException>(() => new Viewport(Vector2.Zero, 1e30f, new Vector2(1e30f, 1)));
		// 1e-30 is positive, but its reciprocal isn't finite enough for an invertible mapping
		Assert.Throws<ArgumentException>(() => new Viewport(Vector2.Zero, 1e-30f, one));
		Assert.True(default(Viewport).IsDefault);
	}

	[Theory]
	[InlineData(0f, 1f)]
	[InlineData(-1f, 1f)]
	[InlineData(float.NaN, 1f)]
	[InlineData(1f, float.PositiveInfinity)]
	public static void LetterboxRejectsInvalidSizes(float x, float y) {
		Vector2 bad = new(x, y), ok = new(10, 10);
		Assert.Equal("contentSize", Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Letterbox(bad, ok, false)).ParamName);
		Assert.Equal("targetPixelSize", Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Letterbox(ok, bad, false)).ParamName);
	}
}
