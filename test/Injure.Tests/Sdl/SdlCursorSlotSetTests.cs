// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Sdl;

namespace Injure.Tests.Sdl;

public static class SdlCursorSlotSetTests {
	private static SdlCursorSlotState add(
		SdlCursorSlotSet set,
		SdlCursorBand band,
		int rank,
		SdlCursor? cursor
	) {
		SdlCursorSlotState s = new(band, rank) { Cursor = cursor };
		set.Add(s);
		return s;
	}

	[Fact]
	public static void NoOpinionsResolveToDefault() {
		SdlCursorSlotSet set = new();
		Assert.Equal(SdlCursor.Default, set.Resolve());
		add(set, SdlCursorBand.Overlay, 5, null);
		Assert.Equal(SdlCursor.Default, set.Resolve());
	}

	[Fact]
	public static void LaterBandsWinRegardlessOfRank() {
		SdlCursorSlotSet set = new();
		add(set, SdlCursorBand.Overlay, int.MinValue, SdlCursor.Crosshair);
		add(set, SdlCursorBand.Content, int.MaxValue, SdlCursor.Pointer);
		Assert.Equal(SdlCursor.Crosshair, set.Resolve());
	}

	[Fact]
	public static void HigherRanksWinWithinABand() {
		SdlCursorSlotSet set = new();
		add(set, SdlCursorBand.Content, 2, SdlCursor.Move);
		add(set, SdlCursorBand.Content, 1, SdlCursor.Pointer);
		Assert.Equal(SdlCursor.Move, set.Resolve());
	}

	[Fact]
	public static void LaterCreatedSlotsWinTies() {
		SdlCursorSlotSet set = new();
		SdlCursorSlotState first = add(set, SdlCursorBand.Content, 0, SdlCursor.Text);
		add(set, SdlCursorBand.Content, 0, SdlCursor.Pointer);
		Assert.Equal(SdlCursor.Pointer, set.Resolve());
		// writing to the older slot doesn't make it win
		first.Cursor = SdlCursor.Wait;
		Assert.Equal(SdlCursor.Pointer, set.Resolve());
	}

	[Fact]
	public static void NoOpinionFallsThroughToLowerSlots() {
		SdlCursorSlotSet set = new();
		add(set, SdlCursorBand.Content, 0, SdlCursor.NwseResize);
		SdlCursorSlotState overlay = add(set, SdlCursorBand.Overlay, 0, SdlCursor.Default);
		// an explicit Default is an opinion, and overrides lower slots
		Assert.Equal(SdlCursor.Default, set.Resolve());
		overlay.Cursor = null;
		Assert.Equal(SdlCursor.NwseResize, set.Resolve());
	}

	[Fact]
	public static void HiddenCompetesLikeAnyCursor() {
		SdlCursorSlotSet set = new();
		add(set, SdlCursorBand.Content, 0, SdlCursor.Pointer);
		SdlCursorSlotState hide = add(set, SdlCursorBand.Content, 1, SdlCursor.Hidden);
		Assert.Equal(SdlCursor.Hidden, set.Resolve());
		set.Remove(hide);
		Assert.Equal(SdlCursor.Pointer, set.Resolve());
	}
}
