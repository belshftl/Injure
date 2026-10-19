// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using Injure.Host;

namespace Injure.Sched.Tickers;

/// <remarks>
/// The <see langword="default"/> value is invalid, since its period is 0.
/// </remarks>
public readonly record struct TickerTiming(HostDuration Period, HostDuration InitialOffset = default) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TickerOverrunMode {
	public enum Case {
		CatchUp = 1,
		Once,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TickerStartMode {
	public enum Case {
		FromCommitTime = 1,
		AtAbsoluteTick,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TickerRetimingMode {
	public enum Case {
		KeepPhase = 1,
		RestartFromCommitTime,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid, since its overrun and start modes are; see
/// <see cref="Default"/>.
/// </remarks>
public readonly record struct TickerOptions(
	int Priority,
	int MaxBurst,
	TickerOverrunMode OverrunMode,
	TickerStartMode StartMode,
	HostTick StartAt
) : IHasInvalidDefault {
	public static readonly TickerOptions Default = new(
		Priority: 0,
		MaxBurst: 8,
		OverrunMode: TickerOverrunMode.Once,
		StartMode: TickerStartMode.FromCommitTime,
		StartAt: default
	);

	bool IHasInvalidDefault.IsDefault => OverrunMode == default;
}

/// <remarks>
/// The <see langword="default"/> value is invalid, since its timing and options are.
/// </remarks>
public readonly record struct TickerSpec(
	TickerTiming Timing,
	TickerOptions Options
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
