// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Sdl;

/// <summary>
/// A mouse cursor an <see cref="SdlCursorSlot"/> can request.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the system cursors of the SDL version Injure ships with, plus <see cref="Hidden"/>. If
/// the system doesn't have one of them, the window shows <see cref="Default"/> instead.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Default"/>.
/// </para>
/// </remarks>
[ClosedEnum]
public readonly partial struct SdlCursor {
	/// <summary>Raw switch tag for <see cref="SdlCursor"/>.</summary>
	public enum Case {
		/// <summary>
		/// The default cursor.
		/// </summary>
		Default,

		/// <summary>
		/// Text selection; usually an I-beam.
		/// </summary>
		Text,

		/// <summary>
		/// Busy; usually an hourglass, watch, or spinning ball.
		/// </summary>
		Wait,

		/// <summary>
		/// Precise-selection crosshair.
		/// </summary>
		Crosshair,

		/// <summary>
		/// Busy in the background while still interactive; usually <see cref="Default"/> +
		/// <see cref="Wait"/>.
		/// </summary>
		Progress,

		/// <summary>
		/// Resizing northwest (up-left) and southeast (down-right); usually a double arrow.
		/// </summary>
		NwseResize,

		/// <summary>
		/// Resizing northeast (up-right) and southwest (down-left); usually a double arrow.
		/// </summary>
		NeswResize,

		/// <summary>
		/// Resizing east (left) and west (right); usually a double arrow.
		/// </summary>
		EwResize,

		/// <summary>
		/// Resizing north (up) and south (down); usually a double arrow.
		/// </summary>
		NsResize,

		/// <summary>
		/// Moving something; usually a four-pointed arrow along the cardinal directions.
		/// </summary>
		Move,

		/// <summary>
		/// The action is not permitted; usually a slashed circle.
		/// </summary>
		NotAllowed,

		/// <summary>
		/// Something clickable; usually a pointing hand.
		/// </summary>
		Pointer,

		/// <summary>
		/// Resizing from the northwest (top left) corner; may be <see cref="NwseResize"/>.
		/// </summary>
		NwResize,

		/// <summary>
		/// Resizing from the north (top) edge; may be <see cref="NsResize"/>.
		/// </summary>
		NResize,

		/// <summary>
		/// Resizing from the northeast (top right) corner; may be <see cref="NeswResize"/>.
		/// </summary>
		NeResize,

		/// <summary>
		/// Resizing from the east (right) edge; may be <see cref="EwResize"/>.
		/// </summary>
		EResize,

		/// <summary>
		/// Resizing from the southeast (bottom right) corner; may be <see cref="NwseResize"/>.
		/// </summary>
		SeResize,

		/// <summary>
		/// Resizing from the south (bottom) edge; may be <see cref="NsResize"/>.
		/// </summary>
		SResize,

		/// <summary>
		/// Resizing from the southwest (bottom left) corner; may be <see cref="NeswResize"/>.
		/// </summary>
		SwResize,

		/// <summary>
		/// Resizing from the west (left) edge; may be <see cref="EwResize"/>.
		/// </summary>
		WResize,

		/// <summary>
		/// No cursor; the cursor is hidden while the pointer is over the window.
		/// </summary>
		Hidden,
	}
}

/// <summary>
/// The coarse priority tier of an <see cref="SdlCursorSlot"/>; a slot in a later band always wins
/// over a slot in an earlier band regardless of either's ranks.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Content"/>.
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // content is the lowest band and what slots default to
public readonly partial struct SdlCursorBand {
	/// <summary>Raw switch tag for <see cref="SdlCursorBand"/>.</summary>
	public enum Case {
		/// <summary>
		/// The game's own content: gameplay, menus, UI, etc.
		/// </summary>
		Content,

		/// <summary>
		/// Things that must win over the game's content no matter how the game orders its own slots,
		/// e.g. debug overlays, dev consoles, and mod menus.
		/// </summary>
		Overlay,
	}
}

/// <summary>
/// One requester's say in which cursor an <see cref="SdlWindow"/> should be showing; created with
/// <see cref="SdlWindow.CreateCursorSlot(SdlCursorBand, int)"/>.
/// </summary>
/// <remarks>
/// <para>
/// A slot holds either a cursor, or <see langword="null"/> for "no opinion". While the pointer is
/// over the window, the window shows the cursor of the winning slot among those that hold one, or
/// <see cref="SdlCursor.Default"/> if none do. The winning slot is the one with the later
/// <see cref="Band"/>, then the higher <see cref="Rank"/>, then the one created later, in that
/// order.
/// </para>
/// <para>
/// <see cref="Cursor"/>'s value stays until it's changed, i.e. is never automatically cleared, so
/// the intended use is to set <see cref="Cursor"/> on every tick to what the current state wants,
/// e.g. a resize cursor while a resizable handle is hovered and <see langword="null"/> otherwise,
/// rather than to set it once when the state changes. Changes apply the next time
/// <see cref="SdlInstance.Events"/> polls or waits, not immediately, so a value that a tick
/// overwrites before the next poll never takes effect.
/// </para>
/// <para>
/// Bound to the thread of its window's <see cref="SdlInstance"/>, like everything else
/// SDL-related.
/// </para>
/// </remarks>
public sealed class SdlCursorSlot : IDisposable {
	private readonly SdlCursorSlotState state;
	private bool disposed;

	internal SdlCursorSlot(SdlWindow window, SdlCursorSlotState state) {
		Window = window;
		this.state = state;
	}

	/// <summary>
	/// The window this slot belongs to.
	/// </summary>
	public SdlWindow Window { get; }

	/// <summary>
	/// The slot's priority band; see <see cref="SdlCursorBand"/>.
	/// </summary>
	public SdlCursorBand Band => state.Band;

	/// <summary>
	/// The slot's priority within <see cref="Band"/>. The highest number wins, <b>not the ordinally
	/// first number.</b>
	/// </summary>
	public int Rank => state.Rank;

	/// <summary>
	/// Whether <see cref="Dispose()"/> has been called, or the window has been disposed.
	/// </summary>
	public bool IsDisposed => disposed || Window.IsDisposed;

	/// <summary>
	/// The cursor this slot is requesting, or <see langword="null"/> for "no opinion". Starts as
	/// <see langword="null"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the window's
	/// <see cref="SdlInstance"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this slot or its window has been disposed.
	/// </exception>
	public SdlCursor? Cursor {
		get {
			chk();
			return state.Cursor;
		}
		set {
			chk();
			state.Cursor = value;
		}
	}

	/// <summary>
	/// Removes this slot from its window.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the window's
	/// <see cref="SdlInstance"/>, unless the window has already been disposed.
	/// </exception>
	public void Dispose() {
		if (IsDisposed) {
			disposed = true;
			return;
		}
		Window.CheckAccess();
		Window.CursorSlots.Remove(state);
		disposed = true;
	}

	private void chk() {
		Window.CheckAccess();
		ObjectDisposedException.ThrowIf(disposed, this);
	}
}

// split out so it's testable without SDL
internal sealed class SdlCursorSlotState(SdlCursorBand band, int rank) {
	public SdlCursorBand Band { get; } = band;
	public int Rank { get; } = rank;
	public SdlCursor? Cursor { get; set; }
}

// a window's cursor slots in creation order
internal sealed class SdlCursorSlotSet {
	private readonly List<SdlCursorSlotState> slots = new();

	public int Count => slots.Count;

	public void Add(SdlCursorSlotState slot) => slots.Add(slot);

	public void Remove(SdlCursorSlotState slot) => slots.Remove(slot);

	public void Clear() => slots.Clear();

	public SdlCursor Resolve() {
		SdlCursorSlotState? best = null;
		// >= so that among equal priorities, the later-created slot wins
		foreach (SdlCursorSlotState s in slots)
			if (s.Cursor is not null && (best is null || compare(s, best) >= 0))
				best = s;
		return best?.Cursor ?? SdlCursor.Default;
	}

	private static int compare(SdlCursorSlotState a, SdlCursorSlotState b) {
		int band = ((int)a.Band.Tag).CompareTo((int)b.Band.Tag);
		return band != 0 ? band : a.Rank.CompareTo(b.Rank);
	}
}
