// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Rendering;

namespace Injure.Runtime;

public readonly struct ServiceConfig {
	public required bool Assets { get; init; }
	public required bool Text { get; init; }
}

public readonly struct WindowConfig {
	public required WindowSettings Settings { get; init; }
	public bool AllowHighDPI { get; init; } = true;

	public WindowConfig() {}
}

public readonly struct RenderConfig {
	public RenderSettings Settings { get; init; } = new();
	public PowerPreference PowerPreference { get; init; } = PowerPreference.HighPerformance;
	public BackendType Backend { get; init; } = BackendType.Null;

	public RenderConfig() {}
}

public readonly struct InputConfig {
	public int MaxBufferedEvents { get; init; } = 8192;

	public InputConfig() {}
}

public readonly struct TimingConfig {
	public required TimingSettings Settings { get; init; }
}

public readonly struct GameConfig {
	public required ServiceConfig Service { get; init; }
	public required WindowConfig Window { get; init; }
	public required TimingConfig Timing { get; init; }
	public RenderConfig Render { get; init; } = new();
	public InputConfig Input { get; init; } = new();

	public GameConfig() {}
}
