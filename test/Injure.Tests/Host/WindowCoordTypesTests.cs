// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Host;

namespace Injure.Tests.Host;

public static class WindowCoordTypesTests {
	[Fact]
	public static void PositionsAndMovementsCombineAffinely() {
		WindowPos a = new(10, 20);
		WindowPos b = new(13, 16);
		WindowDelta d = b - a;
		Assert.Equal(new WindowDelta(3, -4), d);
		Assert.Equal(b, a + d);
		Assert.Equal(a, b - d);
		Assert.Equal(new WindowDelta(6, -8), d * 2);
		Assert.Equal(new WindowDelta(6, -8), 2 * d);
		Assert.Equal(new WindowDelta(1.5f, -2), d / 2);
		Assert.Equal(new WindowDelta(-3, 4), -d);
		Assert.Equal(WindowDelta.Zero, d - d);
		Assert.Equal(new WindowDelta(6, -8), d + d);
	}

	[Fact]
	public static void RawRoundtripsWithoutMapping() {
		Assert.Equal(new Vector2(1.5f, -2), new WindowPos(1.5f, -2).ToRaw());
		Assert.Equal(new WindowPos(1.5f, -2), WindowPos.FromRaw(new Vector2(1.5f, -2)));
		Assert.Equal(new Vector2(3, 4), new WindowDelta(3, 4).ToRaw());
		Assert.Equal(new WindowDelta(3, 4), WindowDelta.FromRaw(new Vector2(3, 4)));
	}

	[Fact]
	public static void FormatsInTheInvariantCulture() {
		Assert.Equal("{ X = 1.5, Y = -2 }", new WindowPos(1.5f, -2).ToString());
		Assert.Equal("{ X = 3.00, Y = 4.00 }", new WindowDelta(3, 4).ToString("F2", null));
	}
}
