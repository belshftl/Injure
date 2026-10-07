// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods;

// use open enums since 1) these are just informational and 2) you can't have the closed enum
// types in an attribute

/// <summary>
/// What thread(s) a plausible-modif-point method runs on; modif code must be prepared to run on
/// those threads. Informative rather than normative.
/// </summary>
public enum PlausibleModifPointThreadAffinity {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified,

	/// <summary>
	/// Runs on a game-defined main thread.
	/// </summary>
	MainThread,

	/// <summary>
	/// Runs on a game-defined render thread.
	/// </summary>
	RenderThread,

	/// <summary>
	/// Runs on a game-defined audio thread.
	/// </summary>
	AudioThread,

	/// <summary>
	/// Runs on one or multiple game-defined worker threads.
	/// </summary>
	WorkerThreads,

	/// <summary>
	/// May run on any arbitrary thread.
	/// </summary>
	AnyThread,

	/// <summary>
	/// Runs on some other thread or multiple other threads not listed in this enum.
	/// See the notes for details.
	/// </summary>
	Other,
}

/// <summary>
/// What kinds of blocking behavior a well-behaved modif for a plausible-modif-point method can
/// exhibit. Informative rather than normative.
/// </summary>
public enum PlausibleModifPointBlockingPolicy {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified,

	/// <summary>
	/// Blocking in a modif of this method is not specifically prohibited beyond standard expectations.
	/// </summary>
	MayBlock,

	/// <summary>
	/// Avoid blocking if possible; blocking may cause apparent stutters or otherwise degrade the
	/// relevant subsystem's functionality.
	/// </summary>
	ShouldNotBlock,

	/// <summary>
	/// Strictly must not block; blocking can snowball into a full failure of the relevant subsystem.
	/// </summary>
	MustNotBlock,
}

/// <summary>
/// Concurrency hazards of a plausible-modif-point method that modifs must be prepared to deal with.
/// Informative rather than normative.
/// </summary>
[Flags]
public enum PlausibleModifPointConcurrencyHazards {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified = 0,

	/// <summary>
	/// None of the specific concurrency hazards listed in this enum apply.
	/// </summary>
	NoKnownHazards = 1 << 0,

	/// <summary>
	/// The method may run while iterating over some global collection/list; modifs must be careful
	/// mutating said collection.
	/// </summary>
	IteratorInvalidationRisk = 1 << 1,

	/// <summary>
	/// The method may run under a held lock/mutex.
	/// </summary>
	RunsUnderLock = 1 << 2,

	/// <summary>
	/// The method may recursively call itself or re-enter the same logical operation on the same
	/// thread.
	/// </summary>
	RecursiveOnSameThread = 1 << 3,

	/// <summary>
	/// The method may be called reentrantly at any point before a previous invocation has completed.
	/// </summary>
	/// <remarks>
	/// Distinct from <see cref="RecursiveOnSameThread"/> since the cause of the reentrant call may not
	/// be known / predictable and can't be easily surrounded by "prepare" / "release locks" / etc.
	/// code the same way a simple recursive call can be.
	/// </remarks>
	Reentrant = 1 << 4,

	/// <summary>
	/// Multiple invocations of this method may run concurrently on different threads.
	/// </summary>
	Concurrent = 1 << 5,

	/// <summary>
	/// The method must not call back into its originating subsystem; doing so may deadlock, recurse,
	/// corrupt state, violate phase rules, double-free, etc.
	/// </summary>
	NoCallbackIntoOriginSubsystem = 1 << 6,

	/// <summary>
	/// The method is called without any synchronization guarantees. Any modifs must assume state not
	/// known to be synchronized right now may be unstable / racing and typical guard locks/mutexes may
	/// not be held.
	/// </summary>
	Unsynchronized = 1 << 7,

	/// <summary>
	/// The method runs in a highly restricted context; see the notes for details. Typically,
	/// this means something like: no allocation, no locks, no blocking, no logging, no throwing,
	/// no subsystem calls not known to be safe. May be as extreme as async-signal-safe contexts.
	/// </summary>
	/// <remarks>
	/// This is expected to be very rarely used, and may be removed, as such procedures are usually
	/// written in native code rather than C#.
	/// </remarks>
	RestrictedExecutionContext = 1 << 8,
}

/// <summary>
/// Responsibilities of a plausible-modif-point method. Modif code must be prepared to correctly
/// handle them / suppress them / etc.
/// </summary>
[Flags]
public enum PlausibleModifPointEffects {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified = 0,

	/// <summary>
	/// None of the specific responsibilities listed in this enum apply.
	/// </summary>
	NoKnownEffects = 1 << 0,

	/// <summary>
	/// The method is pure or a simple query method; modifs should be careful introducing non-trivial
	/// extra work or side effects.
	/// </summary>
	PureOrQuery = 1 << 1,

	/// <summary>
	/// The method may mutate object-local state in a way that other code depends on.
	/// </summary>
	/// <remarks>
	/// Considered for removal; a good portion of non-pure methods do that.
	/// </remarks>
	LocalState = 1 << 2,

	/// <summary>
	/// The method may depend on game-defined global gameplay/world/entity state or mutate it
	/// in a way that other code depends on.
	/// </summary>
	/// <remarks>
	/// Considered for removal; a lot of game code does that.
	/// </remarks>
	WorldState = 1 << 3,

	/// <summary>
	/// The method may perform filesystem or external I/O.
	/// </summary>
	IO = 1 << 4,

	/// <summary>
	/// The method may perform creation/disposal of held resources, generally
	/// activate/deactivate/manage long-lived objects, or do other lifetime-sensitive work.
	/// Carelessly suppressing or removing logic may leave resources dangling or uninitialized.
	/// </summary>
	Lifetime = 1 << 5,

	/// <summary>
	/// The method may touch render state / GPU resources and/or perform and submit draw calls.
	/// </summary>
	/// <remarks>
	/// Considered for removal; a lot of game code does that.
	/// </remarks>
	Rendering = 1 << 6,

	/// <summary>
	/// The method may touch audio state / resources or audio callback logic.
	/// </summary>
	/// <remarks>
	/// Considered for removal; a lot of game code does that.
	/// </remarks>
	Audio = 1 << 7,

	/// <summary>
	/// The method may enqueue/retime/cancel timers, tickers, coroutines, etc. that are managed through
	/// engine APIs or otherwise touch state related to them.
	/// </summary>
	EngineScheduling = 1 << 8,

	/// <summary>
	/// The method may enqueue/retime/cancel jobs, timers, tasks, threads, etc. that are not managed
	/// by the engine (or even known by the engine) or otherwise touch state related to them.
	/// </summary>
	ExternalScheduling = 1 << 9,

	/// <summary>
	/// The method may touch network/session/replication state or perform network I/O.
	/// </summary>
	Networking = 1 << 10,

	/// <summary>
	/// The method may touch save/load logic or durable long-term state.
	/// </summary>
	Persistence = 1 << 11,

	/// <summary>
	/// The method may consume/reseed RNG or otherwise affect deterministic simulation.
	/// </summary>
	Randomness = 1 << 12,

	/// <summary>
	/// The method may invoke user/mod/game callbacks, event handlers, delegates, virtual methods,
	/// scripts, or other arbitrary externally provided code.
	/// Modifs must treat said code as a black-box that may do anything not contractually prohibited by
	/// the original method, such as reenter subsystems, throw, mutate global state, block, spawn
	/// threads/children, etc.
	/// </summary>
	CallsUserCode = 1 << 13,

	/// <summary>
	/// Throwing inside or through the method may leave state partially mutated, break caller
	/// expectations/invariants, skip cleanup, deadlock, etc. Modifs must be careful to catch
	/// exceptions from other methods and not throw any themselves.
	/// </summary>
	/// <remarks>
	/// Does <i>not</i> include unwind-across-FFI-boundary risk, since that's "throwing at any point
	/// while the method's still on the stack"; for that case, see <see cref="FfiOrExternalState"/>.
	/// </remarks>
	ExceptionSensitive = 1 << 14,

	/// <summary>
	/// The method may interface with other native/external code or touch potentially process-global
	/// external state.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This includes methods that do a native/unmanaged call that calls back into managed, since
	/// an exception unwinding across an FFI boundary is dangerous and may abort the process.
	/// </para>
	/// <para>
	/// This does <i>not</i> cover the method being able to generally cause UB. As such, lower-level
	/// unsafe interop code may typically want to couple this with <see cref="UndefinedBehaviorRisk"/>.
	/// </para>
	/// </remarks>
	FfiOrExternalState = 1 << 15,

	/// <summary>
	/// The method may cause C-style undefined behavior if misused or internally broken by a modif.
	/// </summary>
	/// <remarks>
	/// Doesn't necessarily imply native interop; may be as simple as an unsafe method that deals
	/// with pointers.
	/// </remarks>
	UndefinedBehaviorRisk = 1 << 16,
}

/// <summary>
/// Marks a plausible/suggested modif point in this binary/DLL. This attribute is purely for
/// information and easier discovery of relevant locations in source code / decompiler output, and
/// serves no functional runtime purpose.
/// </summary>
/// <remarks>
/// This does <b>not</b> imply in any way that the marked method is a stable API; it may be removed,
/// have its signature/name changed, etc. in future builds without notice. This is merely an
/// informational marker for a plausible modif point in a specific build of a game, intended to be
/// discoverable by mod authors in e.g decompilations. If you are the game developer and want to
/// expose a stable API for mods, don't rely on modifs.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = true, Inherited = false)]
public sealed class PlausibleModifPointAttribute : Attribute {
	public PlausibleModifPointThreadAffinity ThreadAffinity { get; init; } = PlausibleModifPointThreadAffinity.Unspecified;
	public PlausibleModifPointBlockingPolicy Blocking { get; init; } = PlausibleModifPointBlockingPolicy.Unspecified;
	public PlausibleModifPointConcurrencyHazards ConcurrencyHazards { get; init; } = PlausibleModifPointConcurrencyHazards.Unspecified;
	public PlausibleModifPointEffects Effects { get; init; } = PlausibleModifPointEffects.Unspecified;

	public string? Purpose { get; init; }
	public string? AlternativeApi { get; init; }
	public string? Notes { get; init; }
}
