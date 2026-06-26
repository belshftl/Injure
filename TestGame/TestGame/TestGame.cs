// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Injure.Assets;
using Injure.Runtime;
using Injure.Draw;
using Injure.Draw.Text;
using Injure.Layers;
using Injure.Mods;
using Injure.Mods.Runtime;
using Injure.Time;
using Injure.Sched.Tickers;

using TestGame.ModApi;

namespace TestGame;

internal sealed class TestApi : ITestGameModApi {
	public void MarkLoaded(string ownerID) {
		Game.Diagnostics.Info($"mod says it got loaded: {ownerID}");
	}
}

public sealed class Game : IGame {
	public const string OwnerID = "TestGame";
	static string IGame.OwnerID => OwnerID;

	public static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");

	public static GameServices GameServices {
		get => field ?? throw new InvalidOperationException("game not initialized yet or already shut down");
		private set;
	}
	public static ITickerRegistry Tickers => GameServices.Tickers;
	public static InputServices Input => GameServices.Input;
	public static LayerStack LayerStack {
		get => field ?? throw new InvalidOperationException("game not initialized yet or already shut down");
		private set;
	}
	public static LayerTagRegistry LayerTagRegistry { get; } = new();
	public static AssetStore Assets => GameServices.Assets;
	public static TextSystem Text => GameServices.Text;
	public static WindowState WindowState => GameServices.Host.Window.State;

	public static ModRuntime<ITestGameModApi> Mods {
		get => field ?? throw new InvalidOperationException("mod runtime not initialized yet");
		private set;
	}
	public static ModFileWatcher ModWatcher {
		get => field ?? throw new InvalidOperationException("mod file watcher not initialized yet");
		private set;
	}
	public static IOwnerDiagnostics Diagnostics => Mods.GameDiagnostics;

	public const string TestFontFilename = "Aileron-Regular.otf";
	public static AssetRef<Font> TestFont {
		get => field ?? throw new InvalidOperationException("game not initialized yet or already shut down");
		private set;
	}

	public static async Task Main() {
		Game g = new();

		string root = AppContext.BaseDirectory;
		string mods = Path.Combine(root, "Mods");
		string cache = Path.Combine(root, ".mod-cache");
		Mods = new(new ModRuntimeOptions<ITestGameModApi> {
			GameOwnerID = OwnerID,
			ModDirectory = mods,
			CacheDirectory = cache,
			ApiFactory = _ => new TestApi(),
			SharedAssemblies = [
				"Injure",
				"Injure.Mods.Runtime",
				"TestGame.ModApi",
				"MonoMod.RuntimeDetour",
				"MonoMod.Utils",
			],
		});
		await Mods.StartAsync(CancellationToken.None);

		ModWatcher = new();
		ModWatcher.Changed += static ev => {
			if (!ev.Reloadable) {
				Diagnostics.Info($"mod '{ev.OwnerID}' changed but isn't reloadable; restart the game to apply changes");
				return;
			}
			Mods.RequestReload(ev.OwnerID);
		};
		ModWatcher.RebuildFrom(Mods.GetWatchSpecs());

		Runner.Run(g, new GameConfig {
			Service = new ServiceConfig { Assets = true, Text = true },
			Window = new WindowConfig { Settings = new WindowSettings { Title = "TestGame", Width = 640, Height = 480 } },
			Timing = new TimingConfig { Settings = new TimingSettings { RenderMode = RenderTimingMode.Capped, TargetFPS = 60.0 } },
		});
	}

	public void Init(GameServices sv) {
		GameServices = sv;
		LayerStack = new(Tickers, Input.Raw);

		Mods.AttachGameActivateBlocking(sv);
		Assets.RegisterSource(OwnerID, new DirectoryAssetSource(OwnerID, AssetsDirectory), "AssetsDirectory");
		Actions.Init();
		LayerTags.Init();
		TestFont = Assets.GetAsset<Font>(new AssetID(OwnerID, TestFontFilename));

		TickerHandle gameplayTicker = Tickers.Add(new TickerSpec(
			Timing: new TickerTiming(MonoTick.PeriodFromHz(60.0)),
			Options: TickerOptions.Default with {
				OverrunMode = TickerOverrunMode.CatchUp
			}
		));
		LayerStack.PushTop(new GameplayLayer(), gameplayTicker);
	}

	public void Render(Canvas cv) {
		LayerStack.Render(cv);
	}

	public void Shutdown() {
		ModWatcher.Dispose();
		Mods.ShutdownOrAbortBlocking();
		GameServices = null!;
	}

	public void BetweenSchedulerTicks() {
		//Mods.AtSafeBoundaryBlocking(); // TODO: figure out where this should properly be called because it's Not here
		Mods.AtLiveBoundaryBlocking();
	}
}
