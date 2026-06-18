// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Core;

[ClosedEnum]
public readonly partial struct WindowMode {
	public enum Case {
		Normal,
		Minimized,
		Maximized,
	}
}

[ClosedEnum]
public readonly partial struct WindowPositioning {
	public enum Case {
		Undefined,
		Centered,
		Explicit,
	}
}

public readonly struct WindowSettings {
	public required string Title { get; init; }
	public required int Width { get; init; }
	public required int Height { get; init; }
	public WindowMode Mode { get; init; } = WindowMode.Normal;
	public WindowPositioning Positioning { get; init; } = WindowPositioning.Undefined;
	public int X { get; init; } = 0;
	public int Y { get; init; } = 0;
	public bool Visible { get; init; } = true;
	public bool Resizable { get; init; } = true;
	public bool Borderless { get; init; } = false;
	public bool Fullscreen { get; init; } = false;

	public WindowSettings() {}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct PresentMode {
	public enum Case {
		TearFree = 1,
		Adaptive,
		LowLatency,
	}
}

public readonly struct RenderSettings {
	public PresentMode PresentMode { get; init; } = PresentMode.TearFree;

	public RenderSettings() {}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct RenderTimingMode {
	public enum Case {
		Capped = 1,
		Uncapped,
	}
}

public readonly struct TimingSettings {
	public required RenderTimingMode RenderMode { get; init; }
	public required double TargetFPS { get; init; }
	public double TargetLoopHz { get; init; } = 480.0;
	public int MaxLoopDeadlineMissByLoopDurations { get; init; } = 4;

	public TimingSettings() {}
}
