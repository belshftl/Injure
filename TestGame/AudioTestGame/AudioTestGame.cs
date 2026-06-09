// SPDX-License-Identifier: MIT

using System;
using System.IO;

using Injure.Core;
using Injure.Graphics;
using Injure.Timing;
using Injure.Scheduling;

using static Injure.Native.Audio;
using System.Runtime.CompilerServices;

namespace AudioTestGame;

public sealed class Game : IGame {
	public const string OwnerID = "AudioTestGame";
	static string IGame.OwnerID => OwnerID;

	public static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");

	public static GameServices GameServices {
		get => field ?? throw new InvalidOperationException("game not initialized yet or already shut down");
		private set;
	}
	public static ITickerRegistry Tickers => GameServices.Tickers;
	public static InputServices Input => GameServices.Input;
	public static LayerServices Layers => GameServices.Layers;
	public static WindowState WindowState => GameServices.Host.Window.State;

	private static IntPtr engine;

	public static void Main() {
		Game g = new();

		Runner.Run(g, new GameConfig(
			Service: new ServiceConfig(Assets: false, Text: false),
			Window: new WindowConfig(new WindowSettings(Title: "AudioTestGame", Width: 640, Height: 480)),
			Render: new RenderConfig(new RenderSettings(PresentMode.Adaptive)),
			Timing: new TimingConfig(new TimingSettings(RenderTimingMode.Capped, TargetFPS: 60.0))
		));
	}

	public void Init(GameServices sv) {
		static void chk(ae_result r) {
			if (r != ae_result.AE_OK)
				throw new InvalidOperationException($"ae_result {r}");
		}

		GameServices = sv;

		if (Unsafe.SizeOf<ae_command>() != 80)
			throw new InvalidOperationException("ae_command is not 80 bytes");

		ae_config config = new() {
			channels = 2,
			command_capacity = 1024,
			max_voices = 256,
		};
		engine = ae_create(in config);
		if (engine == IntPtr.Zero)
			throw new InvalidOperationException("ae_create failed");

		chk(ae_start_jack(engine, "audio-test", autoconnect: 1));

		ae_command cmd = new() {
			kind = ae_command_kind.AE_COMMAND_SET_TEST_TONE,
			size = (uint)Unsafe.SizeOf<ae_command>(),
			target_frame = AE_COMMAND_IMMEDIATE,
			data = new ae_command_data {
				set_test_tone = new ae_set_test_tone_command {
					enabled = 1,
					hz = 440f,
					gain = 0.1f,
				},
			},
		};
		chk(ae_enqueue_command(engine, in cmd));

		TickerHandle logTicker = Tickers.Add(new TickerSpec(
			Timing: new TickerTiming(MonoTick.PeriodFromHz(2.0)),
			Options: TickerOptions.Default with {
				OverrunMode = TickerOverrunMode.Once
			}
		));
		logTicker.Subscribe((in _, in _) => {
			chk(ae_get_stats(engine, out ae_stats s));
			Console.WriteLine($"sr={s.sample_rate} q={s.quantum_frames} frame={s.frame_cursor} xruns={s.xrun_count} cpu={s.cpu_load:0.00}%");
		});
	}

	public void Render(Canvas cv) {
	}

	public void Shutdown() {
		ae_destroy(engine);
		GameServices = null!;
	}
}
