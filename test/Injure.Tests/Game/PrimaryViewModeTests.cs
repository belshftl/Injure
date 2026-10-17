// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Game;

namespace Injure.Tests.Game;

public static class PrimaryViewModeTests {
	[Fact]
	public static void ModesCompareByValue() {
		Vector2 size = new(320, 180);
		Assert.Equal(PrimaryViewMode.Letterbox(size), PrimaryViewMode.Letterbox(size));
		Assert.NotEqual(PrimaryViewMode.Letterbox(size), PrimaryViewMode.Letterbox(size, integerScale: true));
		Assert.NotEqual(PrimaryViewMode.LogicalUnits, PrimaryViewMode.PhysicalPixels);
		Assert.True(default(PrimaryViewMode).IsDefault);
		Assert.False(PrimaryViewMode.LogicalUnits.IsDefault);
	}

	[Theory]
	[InlineData(0f, 180f)]
	[InlineData(320f, float.NaN)]
	public static void LetterboxRejectsInvalidSizes(float x, float y) {
		Vector2 size = new(x, y);
		ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => PrimaryViewMode.Letterbox(size));
		Assert.Equal("contentSize", ex.ParamName);
	}
}
