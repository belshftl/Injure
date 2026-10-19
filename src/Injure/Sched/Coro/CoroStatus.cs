// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Sched.Coro;

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct CoroStatus {
	public enum Case {
		Running = 1,
		Paused,
		Completed,
		Cancelled,
		Faulted,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct CoroCancellationReason {
	public enum Case {
		ManualStop = 1,
		ScopeCancelled,
		FaultPropagation,
		OwnerRemoved,
		SchedulerDisposed,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct CoroUpdatePhase {
	public enum Case {
		Update = 1,
	}
}
