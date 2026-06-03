// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Analyzers.Lifetime;

internal enum LifetimeObligationKind {
	Hook,
	ILHook,
	NativeHook,
	Thread,
	Timer,
	PeriodicTimer,
	CancellationTokenSource,
	AssemblyLoadContext,
	Process,
	StartedTask,
	ThreadPoolWorkItem,
	Attributed,
}

internal enum ObligationState {
	Uncertain,
	ExceptionLeaked,
	Leaked,
	Satisfied,
}

// mirrors Injure.ModKit.Abstractions.CodeAnalysis.ObligationSatisfactionLevel
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

internal enum FlowMergeKind {
	Conditional,
	LoopIteration,
	Goto,
	TryCatch,
	TryFinally,
	Snapshot,
}

[Flags]
internal enum ObligationPathState {
	None                = 0,
	Absent              = 1 << 0,
	Open                = 1 << 1,
	Satisfied           = 1 << 2,
	MethodSatisfied     = 1 << 3,
	GenerationSatisfied = 1 << 4,
	Leaked              = 1 << 5,
	ExceptionLeaked     = 1 << 6,
}

internal enum ObligationLeakKind {
	AllKnownPaths,
	OnlySomePaths,
}
