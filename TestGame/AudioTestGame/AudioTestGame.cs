// SPDX-License-Identifier: MIT

using System;
using System.IO;

using Injure.Core;
using Injure.Graphics;
using Injure.Timing;
using Injure.Scheduling;

using static Injure.Native.Audio;

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

	private static ae_engine_ptr engine;
	private static ae_sound_id sound;
	private static ae_voice_id voice;

	public static void Main() {
		Game g = new();

		Runner.Run(g, new GameConfig(
			Service: new ServiceConfig(Assets: false, Text: false),
			Window: new WindowConfig(new WindowSettings(Title: "AudioTestGame", Width: 640, Height: 480)),
			Render: new RenderConfig(new RenderSettings(PresentMode.Adaptive)),
			Timing: new TimingConfig(new TimingSettings(RenderTimingMode.Capped, TargetFPS: 60.0))
		));
	}

	private static void chk(ae_result r) {
		if (r != ae_result.AE_OK)
			throw new InvalidOperationException($"ae_result {r}");
	}

	public void Init(GameServices sv) {
		GameServices = sv;

		ae_config config = new() {
			channels = 2,
			command_capacity = 1024,
			maintenance_capacity = 1024,
		};
		chk(ae_create(in config, out engine));
		chk(ae_start_jack(engine, "audio-test", autoconnect: 1));
		chk(ae_get_stats(engine, out ae_stats initial));

		ae_sound_desc desc = new() {
			channels = 2,
			sample_rate = initial.sample_rate,
			frame_count = initial.sample_rate,
			format = ae_sample_format.AE_SAMPLE_FORMAT_F32,
			flags = 0,
		};
		chk(ae_sound_alloc(engine, in desc, out sound));
		chk(ae_sound_get_buffer(engine, sound, out ae_sound_mapping mapping));

		int sampleCount = checked((int)(mapping.byte_length / sizeof(float)));
		Span<float> pcm;
		unsafe {
			pcm = new Span<float>(mapping.data, sampleCount);
		}
		for (int frame = 0; frame < initial.sample_rate; frame++) {
			float sample = MathF.Sin(frame * MathF.Tau * 440.0f / initial.sample_rate) * 0.05f;
			pcm[frame * 2] = sample;
			pcm[frame * 2 + 1] = sample;
		}

		chk(ae_sound_commit(engine, sound));

		ae_play_voice_desc vdesc = new() {
			sound = sound,
			start_frame = ae_optional_mix_frame.IMMEDIATE,
			source_frame = 0,
			gain = 1f,
			playback_rate = 1f,
			flags = ae_voice_flags.AE_VOICE_FLAG_LOOP,
		};
		chk(ae_voice_play(engine, in vdesc, out voice));

		TickerHandle logTicker = Tickers.Add(new TickerSpec(
			Timing: new TickerTiming(MonoTick.PeriodFromHz(4.0)),
			Options: TickerOptions.Default with {
				OverrunMode = TickerOverrunMode.Once
			}
		));
		logTicker.Subscribe((in _, in _) => {
			chk(ae_get_stats(engine, out ae_stats s));
			Console.WriteLine($"sr={s.sample_rate} q={s.quantum_frames} mix={s.mix_frame} xruns={s.xrun_count} cpu={s.cpu_load:0.00}%");
		});
	}

	public void Render(Canvas cv) {
	}

	public void Shutdown() {
		chk(ae_voice_stop(engine, voice));
		System.Threading.Thread.Sleep(50);
		chk(ae_collect_garbage(engine));
		chk(ae_sound_free(engine, sound));
		chk(ae_stop(engine));
		ae_destroy(engine);
		GameServices = null!;
	}
}
