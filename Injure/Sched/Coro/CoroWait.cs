// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Threading;

namespace Injure.Sched.Coro;

public static class CoroWaits {
	public static CoroWaitForTicks Ticks(CoroutineTick ticks) =>
		ticks >= CoroutineTick.Zero ? new CoroWaitForTicks(ticks) : throw new ArgumentOutOfRangeException(nameof(ticks));
	public static CoroWaitForTicks Ticks(int ticks) => Ticks((CoroutineTick)ticks); // quality of life overload for int literals
	public static CoroWaitForSeconds Seconds(double seconds) =>
		seconds >= 0 ? new CoroWaitForSeconds(seconds) : throw new ArgumentOutOfRangeException(nameof(seconds));
	public static CoroWaitForHandle ForHandle(CoroutineHandle handle, bool propagateFault = true, bool throwOnChildCancelled = false) =>
		new(handle, propagateFault, throwOnChildCancelled);
	public static CoroWaitUntilPredicate Until(Func<bool> predicate, string? debugDesc = null) =>
		new(predicate ?? throw new ArgumentNullException(nameof(predicate)), invert: false, debugDesc);
	public static CoroWaitUntilPredicate While(Func<bool> predicate, string? debugDesc = null) =>
		new(predicate ?? throw new ArgumentNullException(nameof(predicate)), invert: true, debugDesc);
}

public sealed class CoroSignal {
	private int val = 0;
	public void Signal() => Interlocked.Exchange(ref val, 1);
	public void Reset() => Interlocked.Exchange(ref val, 0);
	public CoroWaitForSignal Wait(string? debugDesc = null) => new(this, debugDesc);
	internal bool TryConsumeSignal() => Interlocked.Exchange(ref val, 0) != 0;
}

public abstract class CoroWait {
	public abstract bool KeepWaiting(in CoroutineContext ctx);
	public virtual void OnCancel(CoroCancellationReason reason) {}
	public abstract string GetDebugWaitDescription();
}

public sealed class CoroWaitForTicks(CoroutineTick ticks) : CoroWait {
	private readonly CoroutineTick total = ticks;
	private CoroutineTick remaining = ticks;
	public override bool KeepWaiting(in CoroutineContext ctx) => remaining > CoroutineTick.Zero && --remaining > CoroutineTick.Zero;
	public override string GetDebugWaitDescription() => $"for {remaining} more ticks (started at {total})";
}

public sealed class CoroWaitUntilTick(CoroutineTick targetTick) : CoroWait {
	private readonly CoroutineTick target = targetTick;
	public override bool KeepWaiting(in CoroutineContext ctx) => ctx.Tick < target;
	public override string GetDebugWaitDescription() => $"until tick {target}";
}

public sealed class CoroWaitForSeconds(double seconds) : CoroWait {
	private readonly double total = seconds;
	private double remaining = seconds;
	public override bool KeepWaiting(in CoroutineContext ctx) => (remaining -= ctx.DeltaTime) > 0f;
	public override string GetDebugWaitDescription() => $"for {Math.Max(remaining, 0f):0.###} more seconds (started at {total:0.###})";
}

public sealed class CoroWaitForHandle(CoroutineHandle handle, bool propagateFault, bool throwOnChildCancelled) : CoroWait {
	private readonly CoroutineHandle handle = handle;
	private readonly bool propagateFault = propagateFault;
	private readonly bool throwOnChildCancelled = throwOnChildCancelled;
	private bool attached = false;

	public CoroutineHandle TargetHandle => handle;

	internal bool EnsureAttached(CoroutineScheduler scheduler) {
		if (attached)
			return true;
		if (!scheduler.TryRetainHandle(handle))
			return false;
		attached = true;
		return true;
	}

	internal void Detach(CoroutineScheduler scheduler) {
		if (!attached)
			return;
		scheduler.ReleaseRetainedHandle(handle);
		attached = false;
	}

	public override bool KeepWaiting(in CoroutineContext ctx) {
		if (handle == ctx.Handle)
			throw new InvalidOperationException($"coroutine {ctx.Handle} tried to wait on its own handle");
		if (!ctx.Scheduler.TryGetInfo(handle, out CoroutineInfo info))
			throw new InvalidOperationException($"failed to get info for coroutine handle {handle}");
		switch (info.Status.Tag) {
		case CoroutineStatus.Case.Running:
		case CoroutineStatus.Case.Paused:
			return true;
		case CoroutineStatus.Case.Completed:
			return false;
		case CoroutineStatus.Case.Cancelled:
			if (throwOnChildCancelled)
				throw new CoroutineCancelledException(handle, info.CancellationReason ?? CoroCancellationReason.ManualStop);
			return false;
		case CoroutineStatus.Case.Faulted:
			if (info.Fault is null)
				throw new InternalStateException("expected Fault to be nonnull on Faulted status");
			if (propagateFault)
				throw new CoroutineChildFaultException(handle, info.Fault);
			return false;
		default:
			throw new UnreachableException();
		}
	}
	public override string GetDebugWaitDescription() => $"for handle {handle}";
}

public sealed class CoroWaitUntilPredicate(Func<bool> predicate, bool invert, string? debugDesc = null) : CoroWait {
	private readonly Func<bool> predicate = predicate;
	private readonly bool invert = invert;
	private readonly string? debugDesc = debugDesc;
	public override bool KeepWaiting(in CoroutineContext ctx) {
		bool v = predicate();
		return invert ? v : !v;
	}
	public override string GetDebugWaitDescription() => debugDesc ?? $"{(invert ? "while" : "until")} predicate returns true";
}

public sealed class CoroWaitForSignal(CoroSignal signal, string? debugDesc = null) : CoroWait {
	private readonly CoroSignal signal = signal;
	private readonly string? debugDesc = debugDesc;
	public override bool KeepWaiting(in CoroutineContext ctx) => !signal.TryConsumeSignal();
	public override string GetDebugWaitDescription() => debugDesc ?? "for a signal";
}
