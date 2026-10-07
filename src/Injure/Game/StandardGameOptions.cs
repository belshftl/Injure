// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Primitives;
using Injure.Rendering;
using Injure.Sdl;

namespace Injure.Game;

/// <summary>
/// Configuration for a <see cref="StandardGame"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, as <see cref="Window"/> and <see cref="MaxFps"/>
/// are required.
/// </remarks>
public readonly struct StandardGameOptions {
	/// <summary>
	/// SDL initialization options.
	/// </summary>
	public SdlInitOptions Sdl { get; init; }

	/// <summary>
	/// Options for the game's window.
	/// </summary>
	public required SdlWindowOptions Window { get; init; }

	/// <summary>
	/// Render rate limit in frames per second, or <see langword="null"/> to render as often as
	/// presentation allows. Can be changed while running with <see cref="StandardGame.SetMaxFps"/>.
	/// </summary>
	/// <remarks>
	/// Also determines the ticker scheduler's per-batch time budget (one frame period, or 1/60th
	/// of a second when uncapped); that budget is currently fixed when the game starts.
	/// </remarks>
	public required double? MaxFps { get; init; }

	/// <summary>
	/// Surface present mode policy. Can be changed while running with
	/// <see cref="StandardGame.SetPresentModePolicy"/>.
	/// </summary>
	public SurfacePresentModePolicy PresentModePolicy { get; init; } =
		SurfacePresentModePolicy.AutoMailbox;

	/// <summary>
	/// The color each frame is cleared to before <see cref="StandardGame.OnRender"/>.
	/// </summary>
	public Color32 ClearColor { get; init; } = Color32.Black;

	/// <summary>
	/// Whether to create an <see cref="Assets.AssetStore"/>; see <see cref="StandardGame.Assets"/>.
	/// </summary>
	public bool Assets { get; init; } = false;

	/// <summary>
	/// Whether to create a <see cref="Draw.Text.TextSystem"/>; see <see cref="StandardGame.Text"/>.
	/// </summary>
	public bool Text { get; init; } = false;

	/// <summary>
	/// How many raw input events the input system retains.
	/// </summary>
	public int MaxBufferedInputEvents { get; init; } = 8192;

	public StandardGameOptions() {
	}

	internal void Validate(string paramName) {
		Window.Validate(paramName);
		if (MaxFps is double fps && (!double.IsFinite(fps) || fps <= 0.0))
			throw new ArgumentOutOfRangeException(paramName, $"MaxFps must be positive and finite, or null, got {fps}");
		if (MaxBufferedInputEvents <= 0)
			throw new ArgumentOutOfRangeException(paramName, $"MaxBufferedInputEvents must be positive, got {MaxBufferedInputEvents}");
	}
}
