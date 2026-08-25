// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;

namespace Injure.Sched.Coro;

public static class CoroWaits {
	public static CoroWaitForTicks Ticks(CoroTick ticks) =>
		ticks >= CoroTick.Zero ? new CoroWaitForTicks(ticks) : throw new ArgumentOutOfRangeException(nameof(ticks));
	public static CoroWaitForTicks Ticks(int ticks) => Ticks((CoroTick)ticks); // quality of life overload for int literals
	public static CoroWaitForSeconds Seconds(double seconds) =>
		seconds >= 0 ? new CoroWaitForSeconds(seconds) : throw new ArgumentOutOfRangeException(nameof(seconds));
	public static CoroWaitForHandle ForHandle(CoroHandle handle, bool propagateFault = true, bool throwOnChildCancelled = false) =>
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
	public abstract bool KeepWaiting(in CoroContext ctx);
	public virtual void OnCancel(CoroCancellationReason reason) {}
	public abstract string GetDebugWaitDescription();
}

public sealed class CoroWaitForTicks(CoroTick ticks) : CoroWait {
	private readonly CoroTick total = ticks;
	private CoroTick remaining = ticks;
	public override bool KeepWaiting(in CoroContext ctx) => remaining > CoroTick.Zero && --remaining > CoroTick.Zero;
	public override string GetDebugWaitDescription() => $"for {remaining} more ticks (started at {total})";
}

public sealed class CoroWaitUntilTick(CoroTick targetTick) : CoroWait {
	private readonly CoroTick target = targetTick;
	public override bool KeepWaiting(in CoroContext ctx) => ctx.Tick < target;
	public override string GetDebugWaitDescription() => $"until tick {target}";
}

public sealed class CoroWaitForSeconds(double seconds) : CoroWait {
	private readonly double total = seconds;
	private double remaining = seconds;
	public override bool KeepWaiting(in CoroContext ctx) => (remaining -= ctx.DeltaTime) > 0f;
	public override string GetDebugWaitDescription() => $"for {Math.Max(remaining, 0f):0.###} more seconds (started at {total:0.###})";
}

public sealed class CoroWaitForHandle(CoroHandle handle, bool propagateFault, bool throwOnChildCancelled) : CoroWait {
	private readonly CoroHandle handle = handle;
	private readonly bool propagateFault = propagateFault;
	private readonly bool throwOnChildCancelled = throwOnChildCancelled;
	private bool attached = false;

	public CoroHandle TargetHandle => handle;

	internal bool EnsureAttached(CoroScheduler scheduler) {
		if (attached)
			return true;
		if (!scheduler.TryRetainHandle(handle))
			return false;
		attached = true;
		return true;
	}

	internal void Detach(CoroScheduler scheduler) {
		if (!attached)
			return;
		scheduler.ReleaseRetainedHandle(handle);
		attached = false;
	}

	public override bool KeepWaiting(in CoroContext ctx) {
		if (handle == ctx.Handle)
			throw new InvalidOperationException($"coroutine {ctx.Handle} tried to wait on its own handle");
		if (!ctx.Scheduler.TryGetInfo(handle, out CoroInfo info))
			throw new InvalidOperationException($"failed to get info for coroutine handle {handle}");
		switch (info.Status.Tag) {
		case CoroStatus.Case.Running:
		case CoroStatus.Case.Paused:
			return true;
		case CoroStatus.Case.Completed:
			return false;
		case CoroStatus.Case.Cancelled:
			if (throwOnChildCancelled)
				throw new CoroCancelledException(handle, info.CancellationReason ?? CoroCancellationReason.ManualStop);
			return false;
		case CoroStatus.Case.Faulted:
			if (info.Fault is null)
				throw new InternalStateException("expected Fault to be nonnull on Faulted status");
			if (propagateFault)
				throw new CoroChildFaultException(handle, info.Fault);
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
	public override bool KeepWaiting(in CoroContext ctx) {
		bool v = predicate();
		return invert ? v : !v;
	}
	public override string GetDebugWaitDescription() => debugDesc ?? $"{(invert ? "while" : "until")} predicate returns true";
}

public sealed class CoroWaitForSignal(CoroSignal signal, string? debugDesc = null) : CoroWait {
	private readonly CoroSignal signal = signal;
	private readonly string? debugDesc = debugDesc;
	public override bool KeepWaiting(in CoroContext ctx) => !signal.TryConsumeSignal();
	public override string GetDebugWaitDescription() => debugDesc ?? "for a signal";
}
