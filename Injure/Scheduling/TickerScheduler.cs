// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Injure.Timing;

namespace Injure.Scheduling;

internal readonly record struct DueTickerCall(TickerCallback? Callback, TickCallbackInfo Info);

internal sealed class ScheduledTicker {
	private readonly TickerOptions options;
	private TickerTiming timing;

	private bool hadCallback;
	private MonoTick lastScheduledAt;
	private MonoTick lastActualAt;
	private uint lastBatchID;
	private int runsThisBatch;

	public MonoTick NextAt { get; private set; }
	public int Priority => options.Priority;
	public ulong InsertionOrder { get; private set; } // tie breaker for deterministic sorting of equal ones
	internal event TickerCallback? CallbackEv;

	public ScheduledTicker(in TickerSpec spec) {
		if (spec.Timing.Period == MonoTick.Zero)
			throw new ArgumentOutOfRangeException(nameof(spec), "period must be nonzero");
		if (spec.Options.OverrunMode == TickerOverrunMode.CatchUp)
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.Options.MaxBurst);
		options = spec.Options;
		timing = spec.Timing;

		hadCallback = false;
		lastScheduledAt = MonoTick.Zero;
		lastActualAt = MonoTick.Zero;
		lastBatchID = 0;
		runsThisBatch = 0;
		NextAt = MonoTick.Zero;
		InsertionOrder = 0;
	}

	public void Activate(MonoTick commitAt, ulong insertionOrder) {
		hadCallback = false;
		lastScheduledAt = MonoTick.Zero;
		lastActualAt = MonoTick.Zero;
		lastBatchID = 0;
		runsThisBatch = 0;
		NextAt = options.StartMode.Tag switch {
			TickerStartMode.Case.FromCommitTime => commitAt + timing.InitialOffset,
			TickerStartMode.Case.AtAbsoluteTick => options.StartAt,
			_ => throw new UnreachableException(),
		};
		InsertionOrder = insertionOrder;
	}

	public void Retime(MonoTick commitAt, in TickerTiming tm, TickerRetimingMode mode) {
		if (tm.Period == MonoTick.Zero)
			throw new ArgumentOutOfRangeException(nameof(tm), "period must be nonzero");

		MonoTick oldPeriod = timing.Period;
		MonoTick oldNextAt = NextAt;
		timing = tm;
		switch (mode.Tag) {
		case TickerRetimingMode.Case.KeepPhase:
			hadCallback = false;
			lastScheduledAt = MonoTick.Zero;
			lastActualAt = MonoTick.Zero;
			lastBatchID = 0;
			runsThisBatch = 0;
			NextAt = commitAt + timing.InitialOffset;
			break;
		case TickerRetimingMode.Case.RestartFromCommitTime:
			if (oldPeriod == MonoTick.Zero)
				throw new InternalStateException("oldPeriod is somehow zero, this should've been rejected earlier");
			if (oldNextAt > commitAt) {
				MonoTick rem = oldNextAt - commitAt;
				UInt128 newrem128 = (UInt128)rem.Value * (UInt128)timing.Period.Value / (UInt128)oldPeriod.Value;
				NextAt = commitAt + checked((MonoTick)(ulong)newrem128);
			} else {
				NextAt = commitAt;
			}
			break;
		}
	}

	public bool TryTakeOneIfDue(MonoTick now, uint batchID, out DueTickerCall call) {
		call = default;
		if (now < NextAt)
			return false;

		MonoTick scheduledAt;
		switch (options.OverrunMode.Tag) {
		case TickerOverrunMode.Case.CatchUp:
			if (lastBatchID != batchID) {
				lastBatchID = batchID;
				runsThisBatch = 0;
			}
			if (runsThisBatch >= options.MaxBurst)
				return false;
			scheduledAt = NextAt;
			NextAt += timing.Period;
			runsThisBatch++;
			break;
		case TickerOverrunMode.Case.Once:
			scheduledAt = NextAt;
			MonoTick missed = (now - scheduledAt) / timing.Period;
			NextAt = scheduledAt + checked(timing.Period * (missed + (MonoTick)1));
			break;
		default:
			throw new UnreachableException();
		}
		TickerCallback? callback = CallbackEv;
		if (callback is null) {
			markCallbackState(scheduledAt, now);
			return true;
		}
		call = new DueTickerCall(callback, makeInfo(scheduledAt, now));
		markCallbackState(scheduledAt, now);
		return true;
	}

	public void ClearCallbacks() {
		CallbackEv = null;
	}

	private TickCallbackInfo makeInfo(MonoTick scheduledAt, MonoTick actualAt) {
		MonoTick previousScheduledAt = hadCallback ? lastScheduledAt : scheduledAt - timing.Period;
		MonoTick previousActualAt = hadCallback ? lastActualAt : actualAt - timing.Period;
		MonoTick elapsed = hadCallback ? actualAt - lastActualAt : timing.Period;
		MonoTick late = actualAt >= scheduledAt ? actualAt - scheduledAt : MonoTick.Zero;

		return new TickCallbackInfo(
			ScheduledAt: scheduledAt,
			ActualAt: actualAt,
			PreviousScheduledAt: previousScheduledAt,
			PreviousActualAt: previousActualAt,
			Period: timing.Period,
			Elapsed: elapsed,
			Late: late
		);
	}

	private void markCallbackState(MonoTick scheduledAt, MonoTick actualAt) {
		lastScheduledAt = scheduledAt;
		lastActualAt = actualAt;
		hadCallback = true;
	}
}

public readonly record struct TickerSchedulerOptions(
	int BatchCallLimit = 64,
	int EventPollInterval = 8,
	MonoTick MaxBatchDuration = default
);

public sealed class TickerScheduler(in TickerSchedulerOptions options) : ITickerRegistry {
	private enum TickerSlotState {
		Empty,
		PendingAdd,
		Active,
	}
	private sealed class TickerSlot {
		public required int Generation;
		public required TickerSlotState State;
		public required ScheduledTicker Scheduled {
			get {
				if (State == TickerSlotState.Empty)
					throw new InternalStateException("empty ticker slot has no scheduled val");
				return field;
			}
			set;
		}
	}

	private enum TickerCommandKind {
		Add,
		Remove,
		Retime,
	}
	private readonly record struct TickerCommand(
		TickerCommandKind Kind,
		TickerHandle Handle,
		TickerTiming Timing = default,
		TickerRetimingMode RetimingMode = default
	);

	private readonly Lock @lock = new();
	private readonly TickerSchedulerOptions options = options;
	private readonly List<TickerSlot> slots = new();
	private readonly List<int> activeSlots = new();
	private readonly List<TickerCommand> pending = new();
	private ulong nextInsertionOrder;
	private uint nextBatchID;

	public TickerHandle Add(in TickerSpec spec) {
		lock (@lock) {
			int slotidx = makeSlot();
			slots[slotidx].State = TickerSlotState.PendingAdd;
			slots[slotidx].Scheduled = new ScheduledTicker(in spec);
			TickerHandle handle = new(this, slotidx, slots[slotidx].Generation);
			pending.Add(new TickerCommand(TickerCommandKind.Add, handle));
			return handle;
		}
	}

	internal bool Remove(TickerHandle handle) {
		lock (@lock) {
			if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
				return false;
			pending.Add(new TickerCommand(TickerCommandKind.Remove, handle));
			return true;
		}
	}

	internal bool Retime(TickerHandle handle, in TickerTiming timing, TickerRetimingMode mode) {
		lock (@lock) {
			if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
				return false;
			pending.Add(new TickerCommand(TickerCommandKind.Retime, handle, timing, mode));
			return true;
		}
	}

	internal TickerSubscriptionHandle Subscribe(TickerHandle handle, TickerCallback callback) {
		lock (@lock) {
			if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
				throw new InvalidOperationException("this ticker has not been found in the registry (already removed?)");
			slots[slotidx].Scheduled.CallbackEv += callback;
			return new TickerSubscriptionHandle(this, handle, callback);
		}
	}

	internal bool Unsubscribe(TickerHandle handle, TickerCallback callback) {
		lock (@lock) {
			if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
				return false;
			slots[slotidx].Scheduled.CallbackEv -= callback;
			return true;
		}
	}

	public void ApplyPending() {
		lock (@lock) {
			MonoTick commitAt = MonoTick.GetCurrent();
			foreach (TickerCommand cmd in pending) {
				if (!tryGetSlot(cmd.Handle, out int slotIndex))
					continue;
				TickerSlot slot = slots[slotIndex];
				switch (cmd.Kind) {
				case TickerCommandKind.Add:
					if (slot.State != TickerSlotState.PendingAdd)
						break;
					slot.Scheduled.Activate(commitAt, nextInsertionOrder++);
					slot.State = TickerSlotState.Active;
					break;
				case TickerCommandKind.Remove:
					if (slot.State == TickerSlotState.Empty)
						break;
					slot.Scheduled.ClearCallbacks();
					slot.State = TickerSlotState.Empty;
					break;
				case TickerCommandKind.Retime:
					if (slot.State == TickerSlotState.Empty)
						break;
					slot.Scheduled.Retime(commitAt, cmd.Timing, cmd.RetimingMode);
					break;
				}
			}
			pending.Clear();
			rebuildActiveSlots();
		}
	}

	public void RunDueTickers() {
		uint batchID;
		lock (@lock)
			batchID = ++nextBatchID;
		int calls = 0;
		MonoTick start = MonoTick.GetCurrent();
		for (;;) {
			DueTickerCall call = default;
			bool tookAny = false;
			lock (@lock) {
				rebuildActiveSlots();
				foreach (int slotIndex in activeSlots) {
					TickerSlot slot = slots[slotIndex];
					if (slot.State != TickerSlotState.Active)
						continue;
					MonoTick now = MonoTick.GetCurrent();
					if (!slot.Scheduled.TryTakeOneIfDue(now, batchID, out call))
						continue;
					tookAny = true;
					break;
				}
			}
			if (!tookAny)
				return;
			if (call.Callback is not null)
				call.Callback(call.Info);
			calls++;
			if (calls >= options.BatchCallLimit)
				return;
			if (options.EventPollInterval > 0 && calls > options.EventPollInterval)
				return;
			if (options.MaxBatchDuration > MonoTick.Zero) {
				MonoTick elapsed = MonoTick.GetCurrent() - start;
				if (elapsed >= options.MaxBatchDuration)
					return;
			}
		}
	}

	public bool TryGetEarliestNextAt(out MonoTick nextAt) {
		lock (@lock) {
			if (activeSlots.Count == 0) {
				nextAt = MonoTick.Zero;
				return false;
			}
			int firstSlot = activeSlots[0];
			nextAt = slots[firstSlot].Scheduled.NextAt;
			return true;
		}
	}

	private int makeSlot() {
		for (int i = 0; i < slots.Count; i++) {
			if (slots[i].State != TickerSlotState.Empty)
				continue;
			slots[i].Generation++;
			slots[i].State = TickerSlotState.Empty;
			return i;
		}
		slots.Add(new TickerSlot { Generation = 1, State = TickerSlotState.Empty, Scheduled = null! });
		return slots.Count - 1;
	}

	private bool tryGetSlot(TickerHandle handle, out int slotidx) {
		slotidx = handle.Slot;
		if (slotidx < 0 || slotidx >= slots.Count)
			return false;
		return slots[slotidx].Generation == handle.Generation;
	}

	private void rebuildActiveSlots() {
		activeSlots.Clear();
		for (int i = 0; i < slots.Count; i++)
			if (slots[i].State == TickerSlotState.Active && slots[i].Scheduled is not null)
				activeSlots.Add(i);
		activeSlots.Sort((a, b) => {
			ScheduledTicker left = slots[a].Scheduled;
			ScheduledTicker right = slots[b].Scheduled;
			int cmp = left.NextAt.CompareTo(right.NextAt);
			if (cmp != 0)
				return cmp;
			cmp = left.Priority.CompareTo(right.Priority);
			if (cmp != 0)
				return cmp;
			cmp = left.InsertionOrder.CompareTo(right.InsertionOrder);
			if (cmp != 0)
				return cmp;
			return a.CompareTo(b);
		});
	}
}
