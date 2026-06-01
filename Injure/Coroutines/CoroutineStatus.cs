// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Coroutines;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct CoroutineStatus {
	public enum Case {
		Running = 1,
		Paused,
		Completed,
		Cancelled,
		Faulted,
	}
}

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

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct CoroUpdatePhase {
	public enum Case {
		Update = 1,
	}
}
