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

public readonly record struct WindowSettings(
	string Title,
	int Width, int Height,
	WindowMode Mode = default,
	WindowPositioning Positioning = default,
	int X = 0, int Y = 0,
	bool Visible = true,
	bool Resizable = true,
	bool Borderless = false,
	bool Fullscreen = false
);

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct PresentMode {
	public enum Case {
		TearFree = 1,
		Adaptive,
		LowLatency,
	}
}

public readonly record struct RenderSettings(
	PresentMode PresentMode
);

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct RenderTimingMode {
	public enum Case {
		Capped = 1,
		Uncapped,
	}
}

public readonly record struct TimingSettings(
	RenderTimingMode RenderMode, double TargetFPS,
	double TargetLoopHz = 480.0, int MaxLoopDeadlineMissByLoopDurations = 4
);
