// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;

namespace Injure.Sdl;

/// <summary>
/// A snapshot of an <see cref="SdlWindow"/>'s state, as of the last window event that went through
/// <see cref="SdlContext.Events"/> or the last immediate setter call.
/// </summary>
/// <remarks>
/// <para>
/// This is the state the engine has been told about, not the state the window system has right now;
/// for example, after <see cref="SdlWindow.RequestSize"/>, <see cref="Width"/> and
/// <see cref="Height"/> only change after the resulting resize event has been polled.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid; snapshots only come from
/// <see cref="SdlWindow.State"/>.
/// </para>
/// </remarks>
public readonly struct SdlWindowState {
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
	public float DisplayScale { get; internal init; }

	public bool HasKeyboardFocus { get; internal init; }

	/// <summary>
	/// Whether the pointer is currently inside the window.
	/// </summary>
	public bool HasPointer { get; internal init; }

	/// <summary>
	/// When this state last changed, on <see cref="SdlContext.Clock"/>.
	/// </summary>
	public HostTick UpdatedAt { get; internal init; }
}
