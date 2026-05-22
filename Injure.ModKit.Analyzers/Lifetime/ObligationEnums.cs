// SPDX-License-Identifier: MIT

namespace Injure.ModKit.Analyzers.Lifetime;

internal enum LifetimeObligationKind {
	Hook,
	ILHook,
	NativeDetour,
	Thread,
	Timer,
	PeriodicTimer,
	CancellationTokenSource,
	AssemblyLoadContext,
	Process,
	StartedTask,
	ThreadPoolWorkItem,
}

internal enum ObligationState {
	Uncertain,
	ExceptionLeaked,
	Leaked,
	Satisfied,
}

internal enum ObligationSatisfactionLevel {
	None = 0,
	Generation = 1,
	Method = 2,
}

internal enum ObligationTransitionCause {
	MayThrow,
	Return,
	Throw,
	AwaitSuspension,
	YieldSuspension,
	MethodEnd,
	DiscardedValue,
	Escape,
	UnsupportedBranch,
	UnsupportedControlFlow,
	UnsupportedBackwardGoto,
}

internal enum ControlExitKind {
	Return,
	Throw,
	Goto,
	Break,
	Continue,
}

internal static class ObligationSatisfactionLevels {
	public static bool Satisfies(ObligationSatisfactionLevel actual, ObligationSatisfactionLevel required) => actual >= required;
}
