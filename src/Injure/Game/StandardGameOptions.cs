// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Diagnostics;
using Injure.Gpu;
using Injure.Mods;
using Injure.Primitives;
using Injure.Sdl;

namespace Injure.Game;

/// <summary>
/// Configuration for a <see cref="StandardGame"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, as <see cref="GameOwnerId"/>,
/// <see cref="Window"/>, and <see cref="MaxFps"/> are required.
/// </remarks>
public readonly struct StandardGameOptions : IHasInvalidDefault {
	/// <summary>
	/// The game's owner ID.
	/// </summary>
	public required string GameOwnerId { get; init; }

	/// <summary>
	/// The diagnostics sinks the game starts with, or <see langword="null"/> for one
	/// <see cref="DefaultDiagnosticsSink"/> with no minimum-severity cutoff. More can be added while
	/// running through <see cref="StandardGame.DiagnosticsSinks"/>.
	/// </summary>
	/// <remarks>
	/// A game may want to pass <c>[new DefaultDiagnosticsSink(DiagnosticSeverity.Info)]</c> in
	/// release builds, since the default configuration logs debug diagnostics too.
	/// </remarks>
	public IReadOnlyList<IDiagnosticsSink>? DiagnosticsSinks { get; init; } = null;

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
	/// presentation allows. Can be changed while running with
	/// <see cref="StandardGame.SetMaxFps(double?)"/>.
	/// </summary>
	/// <remarks>
	/// Also determines the ticker scheduler's per-batch time budget (one frame period, or 1/60th of a
	/// second when uncapped); that budget is currently fixed when the game starts.
	/// </remarks>
	public required double? MaxFps { get; init; }

	/// <summary>
	/// Options for the GPU device.
	/// </summary>
	/// <remarks>
	/// <see cref="GpuDeviceOptions.CompatibleHost"/> is ignored; the device is always created to be
	/// compatible with the window. Defaults to no features and a high-performance adapter.
	/// </remarks>
	public GpuDeviceOptions GpuDevice { get; init; } = new() { RequiredFeatures = GpuFeatures.None };

	/// <summary>
	/// Surface present mode policy. Can be changed while running with
	/// <see cref="StandardGame.SetPresentModePolicy(SurfacePresentModePolicy)"/>.
	/// </summary>
	public SurfacePresentModePolicy PresentModePolicy { get; init; } =
		SurfacePresentModePolicy.AutoMailbox;

	/// <summary>
	/// The color each frame is cleared to before <see cref="StandardGame.OnRender(RenderFrame)"/>.
	/// </summary>
	public SrgbStraight32 ClearColor { get; init; } = SrgbStraight32.Black;

	/// <summary>
	/// What a drawable unit on the primary target should correspond to: a logical unit (i.e. window
	/// coordinate), a physical pixel, or a unit of a letterboxed fixed-size area. See
	/// <see cref="StandardGame.PrimaryViewport"/>.
	/// </summary>
	public PrimaryViewMode PrimaryViewMode { get; init; } = PrimaryViewMode.LogicalUnits;

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
		if (!ModMetadataValidation.ValidateOwnerId(GameOwnerId, out string? ownerErr))
			throw new ArgumentException($"GameOwnerId is invalid: {ownerErr}", paramName);
		if (DiagnosticsSinks is not null && DiagnosticsSinks.Any(static s => s is null))
			throw new ArgumentException("DiagnosticsSinks must not contain null", paramName);
		Window.Validate(paramName);
		if (PrimaryViewMode.IsDefault)
			throw new ArgumentInvalidDefaultException("PrimaryViewMode must not be the default value", paramName);
		if (MaxFps is double fps && (!double.IsFinite(fps) || fps <= 0.0))
			throw new ArgumentOutOfRangeException(paramName, $"MaxFps must be positive and finite, or null, got {fps}");
		if (MaxBufferedInputEvents <= 0)
			throw new ArgumentOutOfRangeException(paramName, $"MaxBufferedInputEvents must be positive, got {MaxBufferedInputEvents}");
	}

	bool IHasInvalidDefault.IsDefault => GameOwnerId is null;
}
