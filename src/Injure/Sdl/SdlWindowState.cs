// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;

namespace Injure.Sdl;

/// <summary>
/// A snapshot of an <see cref="SdlWindow"/>'s state, as of the last window event that went through
/// <see cref="SdlInstance.Events"/> or the last immediate setter call.
/// </summary>
/// <remarks>
/// <para>
/// This is the state the engine has been told about, not the state the window system has right now;
/// for example, after <see cref="SdlWindow.RequestSize(int, int)"/>, <see cref="Width"/> and
/// <see cref="Height"/> only change after the resulting resize event has been polled.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid; snapshots only come from
/// <see cref="SdlWindow.State"/>.
/// </para>
/// </remarks>
public readonly struct SdlWindowState : IHasInvalidDefault {
	internal SdlWindowState(string title) {
		Title = title;
	}

	/// <summary>
	/// Window title.
	/// </summary>
	public string Title { get; internal init; }

	/// <summary>
	/// Width in screen coordinates.
	/// </summary>
	public int Width { get; internal init; }

	/// <summary>
	/// Height in screen coordinates.
	/// </summary>
	public int Height { get; internal init; }

	/// <summary>
	/// Width in physical pixels; different from <see cref="Width"/> on HiDPI displays.
	/// </summary>
	public int PixelWidth { get; internal init; }

	/// <summary>
	/// Height in physical pixels; different from <see cref="Height"/> on HiDPI displays.
	/// </summary>
	public int PixelHeight { get; internal init; }

	/// <summary>
	/// Horizontal position in screen coordinates.
	/// </summary>
	public int X { get; internal init; }

	/// <summary>
	/// Vertical position in screen coordinates.
	/// </summary>
	public int Y { get; internal init; }

	public bool Visible { get; internal init; }
	public bool Resizable { get; internal init; }
	public bool Borderless { get; internal init; }
	public bool Fullscreen { get; internal init; }
	public SdlWindowMode Mode { get; internal init; }

	/// <summary>
	/// Content scale of the display the window is on, e.g. <c>2.0</c> for a 200% scaled display.
	/// </summary>
	/// <remarks>
	/// This is, in total, how many physical pixels content should occupy per pixel it would occupy at
	/// 100% scaling, based on the current system configuration. Equal to <c>0</c> if SDL failed to
	/// report it.
	/// </remarks>
	public float DisplayScale { get; internal init; }

	/// <summary>
	/// Physical pixels per window coordinate, e.g. <c>2.0</c> on a typical macOS HiDPI display and
	/// <c>1.0</c> on Windows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the factor between window coordinates (what pointer positions and
	/// <see cref="Width"/>/<see cref="Height"/> are in) and physical pixels (what
	/// <see cref="PixelWidth"/>/<see cref="PixelHeight"/> and the primary target are in), and it
	/// defines the size of a logical unit. <see cref="Draw.Viewport.FromWindow(WindowPos, float)"/>
	/// takes this in as <c>pixelDensity</c>.
	/// </para>
	/// <para>
	/// Read from SDL (<c>SDL_GetWindowPixelDensity</c>) on window creation and on every resize, pixel
	/// size change, and display scale change. May be fractional, e.g. <c>1.5</c> with fractional
	/// scaling on Wayland. Equal to <c>1.0</c> if <see cref="SdlWindowOptions.HighPixelDensity"/> is
	/// off, or the platform scales the window itself.
	/// </para>
	/// <para>
	/// Always finite and positive; if SDL reports an unusable value (e.g. while a minimized window
	/// has a zero size), the previous value is kept, starting from <c>1.0</c>.
	/// </para>
	/// </remarks>
	public float PixelDensity { get; internal init; }

	/// <summary>
	/// How much to scale content in logical units to match <see cref="DisplayScale"/>;
	/// <see cref="DisplayScale"/> divided by <see cref="PixelDensity"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the part of the display scale that <see cref="PixelDensity"/> doesn't already apply.
	/// On macOS it's usually <c>1.0</c>, since the OS expresses scaling as pixel density. On
	/// Windows, pixel density is always <c>1.0</c> and the display scale is used for all content
	/// scaling, e.g. <c>1.5</c> at 150%.
	/// </para>
	/// <para>
	/// A game that wants UIs to be scaled in accordance with the user's display scaling setting can
	/// multiply its logical sizes by this.
	/// </para>
	/// <para>
	/// Equal to <c>0</c> if <see cref="DisplayScale"/> is.
	/// </para>
	/// </remarks>
	public float ContentScale => DisplayScale / PixelDensity;

	public bool HasKeyboardFocus { get; internal init; }

	/// <summary>
	/// Whether the pointer is currently inside the window.
	/// </summary>
	public bool HasPointer { get; internal init; }

	/// <summary>
	/// When this state last changed, on <see cref="SdlInstance.Clock"/>.
	/// </summary>
	public HostTick UpdatedAt { get; internal init; }

	bool IHasInvalidDefault.IsDefault => Title is null;
}
