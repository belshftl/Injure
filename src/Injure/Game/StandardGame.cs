// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Diagnostics;
using Injure.Draw;
using Injure.Draw.Text;
using Injure.Host;
using Injure.Input;
using Injure.Mods;
using Injure.Gpu;
using Injure.Sched.Tickers;
using Injure.Sdl;

namespace Injure.Game;

/// <summary>
/// A convenience base class that sets up a single SDL window, a WebGPU device and surface, and the
/// usual engine services, and runs a standard main loop over them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Run()"/> must be called on the process's main thread, which is also the thread every
/// hook runs on.
/// </para>
/// <para>
/// Each iteration of the loop: handles pending events (input, surface resizing, then
/// <see cref="OnEvent(in HostEvent)"/>), reaches the safe boundary for asset reloads, runs due
/// tickers, renders if a frame is due, and then waits until the next frame/ticker/event (whichever
/// comes first).
/// </para>
/// <para>
/// Everything this class does is built from public APIs (<see cref="SdlInstance"/>,
/// <see cref="SdlWindow"/>, <see cref="IHostEventSource"/>, <see cref="HostWait"/>,
/// <see cref="FixedRatePacer"/>, <see cref="TickerScheduler"/>, ...); a game that needs a different
/// structure is expected to write its own loop from those instead of customizing this one.
/// </para>
/// </remarks>
public abstract class StandardGame {
	private sealed class Session {
		public required SdlInstance Sdl;
		public required SdlWindow Window;
		public required GpuDevice GpuDevice;
		public required SurfaceRenderOutput RenderOutput;
		public required TickerScheduler Tickers;
		public required InputSystem Input;
		public required ActionRegistry Actions;
		public required AssetStore? Assets;
		public required AssetThreadAttachment? AssetAttachment;
		public required TextSystem? Text;
		public FixedRatePacer? RenderPacer; // null = uncapped
	}

	private readonly StandardGameOptions options;
	private readonly IOwnerDiagnostics engineDiagnostics;
	private Session? session;
	private int quitRequested;
	private int running;

	/// <summary>
	/// Creates the game with the given options. Nothing is initialized until <see cref="Run()"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="options"/> is invalid; see <see cref="StandardGameOptions"/>.
	/// </exception>
	protected StandardGame(in StandardGameOptions options) {
		options.Validate(nameof(options));
		this.options = options;
		DiagnosticsSinks = new DiagnosticsSinkRegistry(options.DiagnosticsSinks ?? [new DefaultDiagnosticsSink()]);
		Diagnostics = DiagnosticsSinks.ForOwner(options.GameOwnerId);
		engineDiagnostics = DiagnosticsSinks.ForOwner(EngineInfo.OwnerId);
	}

	// ==========================================================================
	// diagnostics, available from construction on

	/// <summary>
	/// The diagnostics sinks of the game, starting with
	/// <see cref="StandardGameOptions.DiagnosticsSinks"/>. The engine's diagnostics go here too, and a
	/// mod runtime should be given this registry (see <c>ModRuntimeOptions.DiagnosticsSinks</c>) so
	/// mods' diagnostics and sinks share it.
	/// </summary>
	protected DiagnosticsSinkRegistry DiagnosticsSinks { get; }

	/// <summary>
	/// Diagnostics attributed to the game, i.e. to <see cref="StandardGameOptions.GameOwnerId"/>.
	/// </summary>
	protected IOwnerDiagnostics Diagnostics { get; }

	// ==========================================================================
	// services, available while Run is executing
	private Session current => session ?? throw new InvalidOperationException("only available while Run is executing, from OnInit until OnShutdown returns");

	/// <summary>
	/// The SDL instance.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected SdlInstance Sdl => current.Sdl;

	/// <summary>
	/// The game's window.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected SdlWindow Window => current.Window;

	/// <summary>
	/// The clock everything in the loop runs on.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if accessed outside of <see cref="Run()"/>, i.e. before <see cref="OnInit()"/> or after
	/// <see cref="OnShutdown()"/> has returned.
	/// </exception>
	protected IHostClock Clock => current.Sdl.Clock;

	/// <summary>
	/// The event source the loop polls and waits on.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected IHostEventSource Events => current.Sdl.Events;

	/// <summary>
	/// The WebGPU device.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected GpuDevice GpuDevice => current.GpuDevice;

	/// <summary>
	/// The ticker registry the loop runs.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected ITickerRegistry Tickers => current.Tickers;

	/// <summary>
	/// Raw input, fed by the loop from <see cref="Events"/>.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected InputSystem Input => current.Input;

	/// <summary>
	/// The action registry.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected ActionRegistry Actions => current.Actions;

	/// <summary>
	/// The asset store, with the built-in asset types registered.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="StandardGameOptions.Assets"/> was not set, or if accessed outside of
	/// <see cref="Run()"/>.
	/// </exception>
	protected AssetStore Assets => current.Assets ?? throw new InvalidOperationException("the asset store is not enabled (StandardGameOptions.Assets)");

	/// <summary>
	/// The main thread's attachment to <see cref="Assets"/>.
	/// </summary>
	/// <inheritdoc cref="Assets" path="/exception"/>
	protected AssetThreadAttachment AssetMainThreadAttachment => current.AssetAttachment ?? throw new InvalidOperationException("the asset store is not enabled (StandardGameOptions.Assets)");

	/// <summary>
	/// The text system.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="StandardGameOptions.Text"/> was not set, or if accessed outside of
	/// <see cref="Run()"/>.
	/// </exception>
	protected TextSystem Text => current.Text ?? throw new InvalidOperationException("the text system is not enabled (StandardGameOptions.Text)");

	/// <summary>
	/// The primary target's drawable area, per <see cref="StandardGameOptions.PrimaryViewMode"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// With <see cref="PrimaryViewMode.LogicalUnits"/>, this is <see cref="Viewport.Whole(Vector2, float)"/>
	/// of the surface at the window's <see cref="SdlWindowState.PixelDensity"/>; with
	/// <see cref="PrimaryViewMode.PhysicalPixels"/>, it's the same but at 1 pixel per unit; with
	/// <see cref="PrimaryViewMode.Letterbox(Vector2, bool)"/>, it's
	/// <see cref="Viewport.Letterbox(Vector2, Vector2, bool)"/> of the content size into the surface.
	/// The surface size is as of the last frame begin (or surface creation, before the first frame).
	/// <see cref="CreateCanvas(RenderFrame)"/> uses this as its <see cref="CanvasTarget.Viewport"/>,
	/// so its <see cref="Viewport.Size"/> is the size of the drawable area in drawable units.
	/// </para>
	/// <para>
	/// The pixel density and the surface size can change between frames (e.g. when the window moves
	/// to another display, or is resized), so read this each frame rather than caching it. Within
	/// <see cref="OnRender(RenderFrame)"/>, it matches the viewport of the canvases created there.
	/// </para>
	/// </remarks>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected Viewport PrimaryViewport => primaryViewport(current);

	/// <summary>
	/// Maps a position in <see cref="Window"/>'s coordinates, e.g. a pointer position, to the primary
	/// target's drawable area, in drawable units.
	/// </summary>
	/// <param name="pos">Position in window coordinates.</param>
	/// <remarks>
	/// This is <see cref="Viewport.FromWindow(WindowPos, float)"/> of <see cref="PrimaryViewport"/> at
	/// the window's pixel density. The result is before any canvas transform.
	/// </remarks>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected Vector2 WindowToDrawable(WindowPos pos) {
		Session ses = current;
		return primaryViewport(ses).FromWindow(pos, ses.Window.State.PixelDensity);
	}

	/// <summary>
	/// Maps a movement in <see cref="Window"/>'s coordinates, e.g. a pointer movement, to drawable
	/// units of the primary target.
	/// </summary>
	/// <param name="delta">Movement in window coordinates.</param>
	/// <remarks>
	/// This is <see cref="Viewport.FromWindow(WindowDelta, float)"/> of <see cref="PrimaryViewport"/>
	/// at the window's pixel density. The result is before any canvas transform.
	/// </remarks>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected Vector2 WindowToDrawable(WindowDelta delta) {
		Session ses = current;
		return primaryViewport(ses).FromWindow(delta, ses.Window.State.PixelDensity);
	}

	// ==========================================================================
	// hooks

	/// <summary>
	/// Called once everything is set up, before the first loop iteration.
	/// </summary>
	/// <remarks>
	/// There is no <c>OnUpdate()</c>; for periodic ticks, you're meant to set up a ticker here.
	/// </remarks>
	protected abstract void OnInit();

	/// <summary>
	/// Called to render a frame into the window.
	/// </summary>
	/// <param name="frame">
	/// The frame, presenting on the window. Owned by the engine, which submits it once this returns.
	/// </param>
	/// <remarks>
	/// Record rendering into <paramref name="frame"/>'s <see cref="RenderFrame.Encoder"/>, or use
	/// <see cref="CreateCanvas(RenderFrame)"/> for 2D drawing; both can be mixed, as long as each
	/// <see cref="OwnedCanvas"/> is disposed before passes are recorded directly.
	/// </remarks>
	protected abstract void OnRender(RenderFrame frame);

	/// <summary>
	/// Called for every event, after the engine's own handling of it (feeding input, resizing the
	/// surface).
	/// </summary>
	/// <remarks>
	/// The default implementation calls <see cref="RequestQuit()"/> on
	/// <see cref="HostEventKind.Quit"/> and on <see cref="HostEventKind.WindowCloseRequested"/> for
	/// <see cref="Window"/>, and does nothing for other events.
	/// </remarks>
	protected virtual void OnEvent(in HostEvent ev) {
		if (ev.Kind == HostEventKind.Quit || (ev.Kind == HostEventKind.WindowCloseRequested && ev.Window == Window.Id))
			RequestQuit();
	}

	/// <summary>
	/// Called once after the loop has ended, before anything is torn down.
	/// </summary>
	/// <remarks>
	/// The default implementation does nothing.
	/// </remarks>
	protected virtual void OnShutdown() {
	}

	// ==========================================================================
	// control

	/// <summary>
	/// Makes the loop end after the current iteration. Callable from any thread.
	/// </summary>
	public void RequestQuit() {
		Volatile.Write(ref quitRequested, 1);
		session?.Sdl.Events.Wake();
	}

	/// <summary>
	/// Changes the render rate limit; <see langword="null"/> renders as often as presentation allows.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="fps"/> is not positive or is not finite.
	/// </exception>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected void SetMaxFps(double? fps) {
		if (fps is double f && (!double.IsFinite(f) || f <= 0.0))
			throw new ArgumentOutOfRangeException(nameof(fps), f, "must be positive and finite, or null");
		Session ses = current;
		ses.RenderPacer = makeRenderPacer(fps, ses.Sdl.Clock.Now);
	}

	/// <summary>
	/// Changes the surface present mode policy; applied when the next frame begins.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected void SetPresentModePolicy(SurfacePresentModePolicy policy) =>
		current.RenderOutput.SetPresentModePolicy(policy);

	// ==========================================================================
	// running

	/// <summary>
	/// Creates an <see cref="OwnedCanvas"/> drawing into <paramref name="frame"/> with
	/// <see cref="PrimaryViewport"/> as the viewport, clearing it to
	/// <see cref="StandardGameOptions.ClearColor"/> first.
	/// </summary>
	/// <param name="frame">The frame passed to <see cref="OnRender(RenderFrame)"/>.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="frame"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The canvas keeps its render pass open, so it must be disposed before recording passes into
	/// <paramref name="frame"/> directly, and before <see cref="OnRender(RenderFrame)"/> returns. Draw
	/// through its <see cref="OwnedCanvas.Canvas"/>.
	/// </para>
	/// <para>
	/// With <see cref="PrimaryViewMode.Letterbox(Vector2, bool)"/>, the bars are outside of the
	/// drawable area, so they keep the clear color. To draw into them, push a target with a
	/// <see cref="Viewport.Whole(Vector2, float)"/> viewport.
	/// </para>
	/// <para>
	/// If <see cref="StandardGameOptions.Text"/> is enabled, the canvas is created with
	/// <see cref="Text"/>, so it can draw text without a prepared layout.
	/// </para>
	/// </remarks>
	protected OwnedCanvas CreateCanvas(RenderFrame frame) {
		ArgumentNullException.ThrowIfNull(frame);
		var p = CanvasParams.Primary(primaryViewport(current), options.ClearColor);
		return new OwnedCanvas(frame, in p, current.Text);
	}

	// read on demand rather than per frame, so it's also correct outside OnRender (e.g. for mapping
	// pointer events); no events are polled during OnRender, so it's stable within a frame
	private Viewport primaryViewport(Session ses) {
		PrimaryViewMode mode = options.PrimaryViewMode;
		Vector2 px = new(ses.RenderOutput.Width, ses.RenderOutput.Height);
		switch (mode.Mode) {
		case PrimaryViewMode.Kind.Invalid:
			throw new InternalStateException("invalid PrimaryViewMode kind");
		case PrimaryViewMode.Kind.LogicalUnits:
			return Viewport.Whole(px, ses.Window.State.PixelDensity);
		case PrimaryViewMode.Kind.PhysicalPixels:
			return Viewport.Whole(px);
		case PrimaryViewMode.Kind.Letterbox:
			// nothing renders at a zero size (e.g. minimized), but the content size should stay readable
			if (px.X == 0f || px.Y == 0f)
				return new Viewport(Vector2.Zero, 1f, mode.ContentSize);
			return Viewport.Letterbox(mode.ContentSize, px, mode.IntegerScale);
		default:
			throw new UnreachableException();
		}
	}

	/// <summary>
	/// Sets everything up, runs the loop until <see cref="RequestQuit()"/> is called, and tears
	/// everything down again.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this game is already running or has already run; each instance runs once.
	/// </exception>
	public void Run() {
		if (Interlocked.Exchange(ref running, 1) != 0)
			throw new InvalidOperationException("a StandardGame instance can only be run once");
		Volatile.Write(ref quitRequested, 0);

		SdlInstance? sdl = null;
		SdlWindow? window = null;
		GpuDevice? gpuDevice = null;
		SurfaceRenderOutput? renderOutput = null;
		AssetStore? assets = null;
		AssetThreadAttachment? assetAttachment = null;
		TextSystem? text = null;
		try {
			sdl = SdlInstance.Init(options.Sdl);
			window = SdlWindow.Create(sdl, options.Window);
			gpuDevice = new GpuDevice(options.GpuDevice with { CompatibleHost = window.SurfaceHost, ErrorHandler = gpuErrorHandler() });
			renderOutput = new SurfaceRenderOutput(gpuDevice, window.SurfaceHost, SurfaceFormatPolicy.NonSrgb, options.PresentModePolicy);
			if (options.Assets) {
				assets = new AssetStore();
				// likely stutters; making the asset's group resident beforehand avoids them
				assets.NonResidentLoad += key => engineDiagnostics.Debug(
					$"{key.AssetType.Name} asset '{key.AssetId}' was loaded synchronously by a borrow or pin, since no residency includes it"
				);
				BuiltinAssetRegistrations.RegisterBaseInto(assets);
				BuiltinAssetRegistrations.RegisterTexture2dInto(assets, gpuDevice);
				assetAttachment = assets.AttachCurrentThread();
			}
			if (options.Text) {
				text = new TextSystem(gpuDevice);
				if (assets is not null)
					BuiltinAssetRegistrations.RegisterFontInto(assets, text);
			}

			var budgetPeriod = HostDuration.PeriodFromHz(options.MaxFps ?? 60.0);
			session = new Session {
				Sdl = sdl,
				Window = window,
				GpuDevice = gpuDevice,
				RenderOutput = renderOutput,
				Tickers = new TickerScheduler(
					sdl.Clock,
					new TickerSchedulerOptions(MaxBatchDuration: budgetPeriod, Budget: TickerBudgetOptions.CreateDefault(budgetPeriod))
				),
				Input = new InputSystem(options.MaxBufferedInputEvents),
				Actions = new ActionRegistry(),
				Assets = assets,
				AssetAttachment = assetAttachment,
				Text = text,
				RenderPacer = makeRenderPacer(options.MaxFps, sdl.Clock.Now),
			};

			OnInit();
			try {
				loop(session);
			} finally {
				OnShutdown();
			}
		} finally {
			session = null;
			assetAttachment?.Dispose();
			// asset versions can hold GPU resources and fonts, so the store goes before those
			assets?.Dispose();
			text?.Dispose();
			renderOutput?.Dispose();
			gpuDevice?.Dispose();
			window?.Dispose();
			sdl?.Dispose();
		}
	}

	private GpuErrorHandler gpuErrorHandler() {
		GpuErrorHandler? configured = options.GpuDevice.ErrorHandler;
		if (configured is not null && !ReferenceEquals(configured, GpuErrorHandlers.FailFast))
			return configured;
		GpuErrorHandler report = GpuErrorHandlers.Report(engineDiagnostics);
		return (device, in error) => {
			try {
				report(device, in error);
			} finally {
				GpuErrorHandlers.FailFast(device, in error);
			}
		};
	}

	private void loop(Session ses) {
		SdlHostClock clock = ses.Sdl.Clock;
		SdlEventSource events = ses.Sdl.Events;
		while (Volatile.Read(ref quitRequested) == 0) {
			while (events.TryPoll(out HostEvent ev)) {
				handleEvent(ses, in ev);
				if (Volatile.Read(ref quitRequested) != 0)
					return;
			}

			// TODO: the mod safe boundary needs its own design
			ses.AssetAttachment?.AtReloadBoundary();
			ses.Assets?.ApplyQueuedReloads();

			ses.Tickers.ApplyPending();
			ses.Tickers.RunDueTickers();
			if (Volatile.Read(ref quitRequested) != 0)
				return;

			if (ses.RenderPacer is not FixedRatePacer pacer) {
				render(ses);
			} else if (pacer.IsDue(clock.Now)) {
				render(ses);
				pacer.Advance(clock.Now);
			}

			// catch tickers added/retimed during rendering, and run ones that became due while rendering
			// instead of having them wait for the next iteration
			ses.Tickers.ApplyPending();
			ses.Tickers.RunDueTickers();
			if (Volatile.Read(ref quitRequested) != 0)
				return;

			// uncapped: no waiting, presentation is what limits the rate
			if (ses.RenderPacer is not FixedRatePacer p)
				continue;
			HostTick deadline = p.Next;
			if (ses.Tickers.TryGetEarliestNextAt(out HostTick nextTick) && nextTick < deadline)
				deadline = nextTick;
			HostWait.Until(events, deadline);
		}
	}

	private void handleEvent(Session ses, in HostEvent ev) {
		ses.Input.TryHandle(in ev);
		OnEvent(in ev);
	}

	private void render(Session ses) {
		if (!RenderFrame.TryBegin(ses.GpuDevice, ses.RenderOutput, out RenderFrame? frame))
			return;
		using (frame) {
			OnRender(frame);
			frame.Submit();
		}
	}

	private static FixedRatePacer? makeRenderPacer(double? fps, HostTick now) =>
		fps is double f ? FixedRatePacer.Skipping(HostDuration.PeriodFromHz(f), now) : null;
}
