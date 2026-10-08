// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Draw;
using Injure.Draw.Text;
using Injure.Host;
using Injure.Input;
using Injure.Rendering;
using Injure.Sched.Tickers;
using Injure.Sdl;

namespace Injure.Game;

/// <summary>
/// A convenience base class that sets up a single SDL window, a WebGPU device and surface, and the
/// usual engine services, and runs a standard main loop over them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Run"/> must be called on the process's main thread, which is also the thread every
/// hook runs on.
/// </para>
/// <para>
/// Each iteration of the loop: handles pending events (input, surface resizing, then
/// <see cref="OnEvent"/>), reaches the safe boundary for asset reloads, runs due tickers, renders
/// if a frame is due, and then waits until the next frame/ticker/event (whichever comes first).
/// </para>
/// <para>
/// Everything this class does is built from public APIs (<see cref="SdlContext"/>,
/// <see cref="SdlWindow"/>, <see cref="IHostEventSource"/>, <see cref="HostWait"/>,
/// <see cref="FixedRatePacer"/>, <see cref="TickerScheduler"/>, ...); a game that needs a
/// different structure is expected to write its own loop from those instead of customizing this
/// one.
/// </para>
/// </remarks>
public abstract class StandardGame {
	private sealed class Session {
		public required SdlContext Sdl;
		public required SdlWindow Window;
		public required WebGpuDevice GpuDevice;
		public required SurfaceRenderOutput RenderOutput;
		public required ViewGlobals ViewGlobals;
		public required CanvasSharedResources CanvasResources;
		public required TickerScheduler Tickers;
		public required InputSystem Input;
		public required ActionRegistry Actions;
		public required EngineResourceStore EngineResources;
		public required AssetStore? Assets;
		public required AssetThreadCtx? AssetCtx;
		public required TextSystem? Text;
		public FixedRatePacer? RenderPacer; // null = uncapped
	}

	private readonly StandardGameOptions options;
	private readonly CanvasParams baseCanvasParams;
	private Session? session;
	private int quitRequested;
	private int running;

	/// <summary>
	/// Creates the game with the given options. Nothing is initialized until <see cref="Run"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="options"/> is invalid; see <see cref="StandardGameOptions"/>.
	/// </exception>
	protected StandardGame(in StandardGameOptions options) {
		options.Validate(nameof(options));
		this.options = options;
		baseCanvasParams = new CanvasParams(
			Target: CanvasTarget.Primary,
			ColorAttachmentOps: ColorAttachmentOps.Clear(options.ClearColor),
			Scissor: CanvasScissor.None,
			Transform: Matrix3x2.Identity,
			OutputState: CanvasOutputStates.Alpha,
			Material: CanvasMaterials.Color
		);
	}

	// ==========================================================================
	// services, available while Run is executing
	private Session current => session ?? throw new InvalidOperationException("only available while Run is executing, from OnInit until OnShutdown returns");

	/// <summary>
	/// The SDL context.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected SdlContext Sdl => current.Sdl;

	/// <summary>
	/// The game's window.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected SdlWindow Window => current.Window;

	/// <summary>
	/// The clock everything in the loop runs on.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if accessed outside of <see cref="Run"/>, i.e. before <see cref="OnInit"/> or after
	/// <see cref="OnShutdown"/> has returned.
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
	protected WebGpuDevice GpuDevice => current.GpuDevice;

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
	/// The engine resource store, with the built-in shaders registered.
	/// </summary>
	/// <inheritdoc cref="Clock" path="/exception"/>
	protected EngineResourceStore EngineResources => current.EngineResources;

	/// <summary>
	/// The asset store, with the built-in asset types registered.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="StandardGameOptions.Assets"/> was not set, or if accessed outside of
	/// <see cref="Run"/>.
	/// </exception>
	protected AssetStore Assets => current.Assets ?? throw new InvalidOperationException("the asset store is not enabled (StandardGameOptions.Assets)");

	/// <summary>
	/// The main thread's asset thread context.
	/// </summary>
	/// <inheritdoc cref="Assets" path="/exception"/>
	protected AssetThreadCtx AssetMainThreadContext => current.AssetCtx ?? throw new InvalidOperationException("the asset store is not enabled (StandardGameOptions.Assets)");

	/// <summary>
	/// The text system.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="StandardGameOptions.Text"/> was not set, or if accessed outside of
	/// <see cref="Run"/>.
	/// </exception>
	protected TextSystem Text => current.Text ?? throw new InvalidOperationException("the text system is not enabled (StandardGameOptions.Text)");

	// ==========================================================================
	// hooks

	/// <summary>
	/// Called once everything is set up, before the first loop iteration.
	/// </summary>
	protected abstract void OnInit();

	/// <summary>
	/// Called to draw a frame into the window.
	/// </summary>
	protected abstract void OnRender(Canvas cv);

	/// <summary>
	/// Called for every event, after the engine's own handling of it (feeding input, resizing the
	/// surface).
	/// </summary>
	/// <remarks>
	/// The default implementation calls <see cref="RequestQuit"/> on
	/// <see cref="HostEventKind.Quit"/> and on <see cref="HostEventKind.WindowCloseRequested"/> for
	/// <see cref="Window"/>.
	/// </remarks>
	protected virtual void OnEvent(in HostEvent ev) {
		if (ev.Kind == HostEventKind.Quit || (ev.Kind == HostEventKind.WindowCloseRequested && ev.Window == Window.Id))
			RequestQuit();
	}

	/// <summary>
	/// Called once after the loop has ended, before anything is torn down.
	/// </summary>
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
	/// Sets everything up, runs the loop until <see cref="RequestQuit"/> is called, and tears
	/// everything down again.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this game is already running or has already run; each instance runs once.
	/// </exception>
	public void Run() {
		if (Interlocked.Exchange(ref running, 1) != 0)
			throw new InvalidOperationException("a StandardGame instance can only be run once");
		Volatile.Write(ref quitRequested, 0);

		SdlContext? sdl = null;
		SdlWindow? window = null;
		WebGpuDevice? gpuDevice = null;
		SurfaceRenderOutput? renderOutput = null;
		ViewGlobals? viewGlobals = null;
		CanvasSharedResources? canvasResources = null;
		AssetThreadCtx? assetCtx = null;
		TextSystem? text = null;
		try {
			sdl = SdlContext.Init(options.Sdl);
			window = SdlWindow.Create(sdl, options.Window);
			gpuDevice = new WebGpuDevice();
			renderOutput = new SurfaceRenderOutput(gpuDevice, window.SurfaceHost, options.PresentModePolicy);
			viewGlobals = new ViewGlobals(gpuDevice, renderOutput.Width, renderOutput.Height);

			EngineResourceStore engineResources = createEngineResources();
			canvasResources = new CanvasSharedResources(gpuDevice, engineResources);

			AssetStore? assets = null;
			if (options.Assets) {
				assets = new AssetStore();
				BuiltinAssetRegistrations.RegisterBaseInto(assets);
				BuiltinAssetRegistrations.RegisterTexture2dInto(assets, gpuDevice);
				assetCtx = assets.AttachCurrentThread();
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
				ViewGlobals = viewGlobals,
				CanvasResources = canvasResources,
				Tickers = new TickerScheduler(
					sdl.Clock,
					new TickerSchedulerOptions(MaxBatchDuration: budgetPeriod, Budget: TickerBudgetOptions.CreateDefault(budgetPeriod))
				),
				Input = new InputSystem(options.MaxBufferedInputEvents),
				Actions = new ActionRegistry(),
				EngineResources = engineResources,
				Assets = assets,
				AssetCtx = assetCtx,
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
			canvasResources?.Dispose();
			text?.Dispose();
			assetCtx?.Dispose();
			viewGlobals?.Dispose();
			renderOutput?.Dispose();
			gpuDevice?.Dispose();
			window?.Dispose();
			sdl?.Dispose();
		}
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

			// TODO: the mod safe boundary needs its own design, for now it's asset reloads only
			ses.AssetCtx?.AtSafeBoundary();
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
		if (ev.Kind == HostEventKind.WindowPixelSizeChanged && ev.Window == ses.Window.Id)
			ses.RenderOutput.Resized();
		OnEvent(in ev);
	}

	private void render(Session ses) {
		if (!ses.RenderOutput.TryBeginFrame(out RenderFrame? frame))
			return;
		using (frame) {
			ses.ViewGlobals.Update(frame.PrimaryView.Width, frame.PrimaryView.Height);
			using (Canvas cv = new(ses.GpuDevice, ses.ViewGlobals, frame, ses.CanvasResources, in baseCanvasParams))
				OnRender(cv);
			frame.SubmitAndPresent();
		}
	}

	private static FixedRatePacer? makeRenderPacer(double? fps, HostTick now) =>
		fps is double f ? FixedRatePacer.Skipping(HostDuration.PeriodFromHz(f), now) : null;

	private static EngineResourceStore createEngineResources() {
		EngineResourceStore store = new();
		store.RegisterSource(
			new EmbeddedEngineResourceSource(
				typeof(StandardGame).Assembly,
				new HashSet<EngineResourceId>(
					[
						BuiltinShaders.Primitive2d.ResourceId,
						BuiltinShaders.Textured2dColor.ResourceId,
						BuiltinShaders.Textured2dRmask.ResourceId,
						BuiltinShaders.Textured2dSdf.ResourceId,
					]
				)
			)
		);
		return store;
	}
}
