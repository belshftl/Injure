// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;

using Injure.Assets;
using Injure.Graphics.Text;
using Injure.Input;
using Injure.Rendering;
using Injure.Scheduling;

using static Injure.Core.GameServiceSharedUtil;

namespace Injure.Core;

internal static class GameServiceSharedUtil {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Alive<T>(GameServiceLifetime lifetime, T obj) {
		if (lifetime.IsShutdown)
			throw new InvalidOperationException("game services are no longer available after shutdown");
		return obj;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T AliveAndNonnull<T>(GameServiceLifetime lifetime, T? obj, string msg) where T : class {
		if (lifetime.IsShutdown)
			throw new InvalidOperationException("game services are no longer available after shutdown");
		if (obj is null)
			throw new InvalidOperationException(msg);
		return obj;
	}
}

internal sealed class GameServiceLifetime {
	private int shutdown = 0;
	public bool IsShutdown {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Volatile.Read(ref shutdown) != 0;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Shutdown() => Volatile.Write(ref shutdown, 1);
}

public sealed class HostServices {
	private readonly GameServiceLifetime lifetime;
	public IWindowController Window => Alive(lifetime, field);
	public IRenderController Render => Alive(lifetime, field);
	public ITimingController Timing => Alive(lifetime, field);

	internal HostServices(GameServiceLifetime lifetime, IWindowController window, IRenderController render, ITimingController timing) {
		this.lifetime = lifetime;
		Window = window;
		Render = render;
		Timing = timing;
	}
}

public sealed class GraphicsServices {
	private readonly GameServiceLifetime lifetime;
	public WebGPUDevice Device => Alive(lifetime, field);

	internal GraphicsServices(GameServiceLifetime lifetime, WebGPUDevice device) {
		this.lifetime = lifetime;
		Device = device;
	}
}

public sealed class InputServices {
	private readonly GameServiceLifetime lifetime;
	public IInputSource Raw => Alive(lifetime, field);
	public ActionRegistry Actions => Alive(lifetime, field);

	internal InputServices(GameServiceLifetime lifetime, IInputSource raw, ActionRegistry actions) {
		this.lifetime = lifetime;
		Raw = raw;
		Actions = actions;
	}
}

public sealed class AdvancedServices {
	private readonly GameServiceLifetime lifetime;
	public EngineResourceStore EngineResources => Alive(lifetime, field);
	public AssetThreadContext AssetMainThreadContext => AliveAndNonnull(lifetime, field, "assets subsystem is not enabled");

	internal AdvancedServices(GameServiceLifetime lifetime, EngineResourceStore engineResources, AssetThreadContext? assetMainThreadCtx) {
		this.lifetime = lifetime;
		EngineResources = engineResources;
		AssetMainThreadContext = assetMainThreadCtx;
	}
}

public sealed class GameServices {
	private readonly GameServiceLifetime lifetime;
	private readonly AssetStore? assets;
	private readonly AssetThreadContext? assetMainThreadCtx;
	private readonly TextSystem? text;

	// required:
	public ITickerRegistry Tickers => Alive(lifetime, field);
	public HostServices Host => Alive(lifetime, field);
	public GraphicsServices Graphics => Alive(lifetime, field);
	public InputServices Input => Alive(lifetime, field);
	public AdvancedServices Advanced => Alive(lifetime, field);

	// optional:
	public AssetStore Assets => AliveAndNonnull(lifetime, assets, "assets subsystem is not enabled");
	public TextSystem Text => AliveAndNonnull(lifetime, text, "text subsystem is not enabled");
	public bool HasAssets => Alive(lifetime, assets is not null);
	public bool HasText => Alive(lifetime, text is not null);

	internal GameServices(
		ITickerRegistry tickers,
		IWindowController windowController,
		IRenderController renderController,
		ITimingController timingController,
		WebGPUDevice gpuDevice,
		IInputSource rawInput,
		ActionRegistry actionRegistry,
		EngineResourceStore engineResources,
		AssetStore? assets,
		AssetThreadContext? assetMainThreadCtx,
		TextSystem? text
	) {
		if (assets is null ^ assetMainThreadCtx is null)
			throw new InternalStateException("was expecting either none of or both the asset store and asset thread context to be null");
		lifetime = new GameServiceLifetime();
		Tickers = tickers;
		Host = new HostServices(lifetime, windowController, renderController, timingController);
		Graphics = new GraphicsServices(lifetime, gpuDevice);
		Input = new InputServices(lifetime, rawInput, actionRegistry);
		Advanced = new AdvancedServices(lifetime, engineResources, assetMainThreadCtx);
		this.assets = assets;
		this.assetMainThreadCtx = assetMainThreadCtx;
		this.text = text;
	}

	internal void AtSafeBoundary() {
		assetMainThreadCtx?.AtSafeBoundary();
		assets?.ApplyQueuedReloads();
	}

	internal void Shutdown() => lifetime.Shutdown();
}
