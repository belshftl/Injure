// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using Injure.Timing;

namespace Injure.Scheduling;

internal struct EwmaMonoTick {
	private MonoTick val;
	private bool initialized;

	public readonly bool HasValue => initialized;
	public readonly MonoTick Value => val;

	public void AddSample(MonoTick sample, int alphaShift) {
		ArgumentOutOfRangeException.ThrowIfNegative(alphaShift);

		if (!initialized) {
			val = sample;
			initialized = true;
			return;
		}

		if (sample >= val)
			val += sample - val >> alphaShift;
		else
			val -= val - sample >> alphaShift;
	}
}

internal sealed class TickerSubscription(TickerCallback callback) {
	public TickerCallback Callback { get; } = callback;
	public EwmaMonoTick RuntimeEwma;
	public MonoTick LastRuntime;
	public ulong InvocationCount;
	public ulong OverrunCount;
}

internal readonly record struct DueTickerCall(TickerSubscription Subscription, TickCallbackTimingInfo Info);

internal sealed class ScheduledTicker {
	private readonly TickerOptions options;
	private readonly List<TickerSubscription> subscriptions = new();
	private TickerTiming timing;

	private bool hadCallback;
	private MonoTick lastScheduledAt;
	private MonoTick lastActualAt;
	private uint lastBatchID;
	private int runsThisBatch;

	public MonoTick NextAt { get; private set; }
	public int Priority => options.Priority;
	public ulong InsertionOrder { get; private set; } // tie breaker for deterministic sorting of equal ones

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

	public TickerSubscription AddSubscription(TickerCallback callback) {
		TickerSubscription subscription = new(callback);
		subscriptions.Add(subscription);
		return subscription;
	}

	public bool RemoveSubscription(TickerSubscription subscription) => subscriptions.Remove(subscription);

	public void ClearSubscriptions() {
		subscriptions.Clear();
	}

	public bool TryTakeOneIfDue(MonoTick now, uint batchID, List<DueTickerCall> calls) {
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

		TickCallbackTimingInfo info = makeInfo(scheduledAt, now);
		foreach (TickerSubscription subscription in subscriptions)
			calls.Add(new DueTickerCall(subscription, info));
		markCallbackState(scheduledAt, now);
		return true;
	}

	private TickCallbackTimingInfo makeInfo(MonoTick scheduledAt, MonoTick actualAt) {
		MonoTick previousScheduledAt = hadCallback ? lastScheduledAt : scheduledAt - timing.Period;
		MonoTick previousActualAt = hadCallback ? lastActualAt : actualAt - timing.Period;
		MonoTick elapsed = hadCallback ? actualAt - lastActualAt : timing.Period;
		MonoTick late = actualAt >= scheduledAt ? actualAt - scheduledAt : MonoTick.Zero;

		return new TickCallbackTimingInfo(
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

public readonly record struct TickerBudgetOptions(
	MonoTick TargetLoopPeriod,
	MonoTick ReservedLoopSlack,
	uint OvercommitNumerator,
	uint OvercommitDenominator,
	MonoTick ColdStartWeight,
	MonoTick MinWeight,
	MonoTick MaxWeight,
	MonoTick MinCallbackBudget,
	MonoTick MaxCallbackBudget,
	int EwmaAlphaShift
) {
	public static TickerBudgetOptions CreateDefault(MonoTick targetLoopPeriod, MonoTick? reservedLoopSlack = null) {
		static MonoTick defaultReservedSlack(MonoTick targetLoopPeriod) {
			// reserve ~10% of the loop period clamped to [1/64, 1/4]
			MonoTick slack = targetLoopPeriod / (MonoTick)10;
			MonoTick min = maxOne(targetLoopPeriod >> 6);
			MonoTick max = targetLoopPeriod >> 2;
			if (slack < min)
				return min;
			if (slack > max)
				return max;
			return slack;
		}
		static MonoTick maxOne(MonoTick value) => value == MonoTick.Zero ? (MonoTick)1 : value;

		if (targetLoopPeriod == MonoTick.Zero)
			throw new ArgumentOutOfRangeException(nameof(targetLoopPeriod), "target loop period must be nonzero");
		MonoTick slack = reservedLoopSlack ?? defaultReservedSlack(targetLoopPeriod);
		if (slack > targetLoopPeriod >> 1) // keep slack sane, at most 1/2 of the full loop
			slack = targetLoopPeriod >> 1;
		return new TickerBudgetOptions(
			TargetLoopPeriod: targetLoopPeriod,
			ReservedLoopSlack: slack,
			OvercommitNumerator: 8,
			OvercommitDenominator: 1,
			ColdStartWeight: targetLoopPeriod >> 6,
			MinWeight: maxOne(targetLoopPeriod >> 12),
			MaxWeight: targetLoopPeriod,
			MinCallbackBudget: maxOne(targetLoopPeriod >> 10),
			MaxCallbackBudget: targetLoopPeriod,
			EwmaAlphaShift: 4
		);
	}

	public static readonly TickerBudgetOptions Default480Hz = CreateDefault(MonoTick.PeriodFromHz(480.0));

	internal TickerBudgetOptions Normalize() {
		TickerBudgetOptions options = Equals(default) ? Default480Hz : this;
		if (options.OvercommitDenominator == 0)
			options = options with { OvercommitDenominator = 1 };
		if (options.OvercommitNumerator == 0)
			options = options with { OvercommitNumerator = 1 };
		if (options.EwmaAlphaShift < 0)
			options = options with { EwmaAlphaShift = 0 };
		if (options.MinWeight == MonoTick.Zero)
			options = options with { MinWeight = new MonoTick(1) };
		if (options.MaxWeight != MonoTick.Zero && options.MaxWeight < options.MinWeight)
			options = options with { MaxWeight = options.MinWeight };
		if (options.MaxCallbackBudget != MonoTick.Zero && options.MaxCallbackBudget < options.MinCallbackBudget)
			options = options with { MaxCallbackBudget = options.MinCallbackBudget };
		return options;
	}
}

public readonly record struct TickerSchedulerOptions(
	int BatchCallLimit = 64,
	int EventPollInterval = 8,
	MonoTick MaxBatchDuration = default,
	TickerBudgetOptions Budget = default
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
	private readonly TickerSchedulerOptions options = options with { Budget = options.Budget.Normalize() };
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
			TickerSubscription subscription = slots[slotidx].Scheduled.AddSubscription(callback);
			return new TickerSubscriptionHandle(this, handle, subscription);
		}
	}

	internal bool Unsubscribe(TickerHandle handle, TickerSubscription subscription) {
		lock (@lock) {
			if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
				return false;
			return slots[slotidx].Scheduled.RemoveSubscription(subscription);
		}
	}

	public void ApplyPending() {
		lock (@lock) {
			var commitAt = MonoTick.GetCurrent();
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
					slot.Scheduled.ClearSubscriptions();
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
		var start = MonoTick.GetCurrent();
		List<DueTickerCall> dueCalls = new();
		List<MonoTick> dueBudgets = new();

		for (;;) {
			bool tookAny = takeNextDueCalls(batchID, dueCalls);
			if (!tookAny)
				return;

			if (dueCalls.Count > 0) {
				planBudgets(dueCalls, dueBudgets);

				for (int i = 0; i < dueCalls.Count; i++)
					invokeDueCall(dueCalls[i], dueBudgets[i]);

				calls += dueCalls.Count;
			} else {
				calls++;
			}

			dueCalls.Clear();
			dueBudgets.Clear();

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

	private bool takeNextDueCalls(uint batchID, List<DueTickerCall> calls) {
		lock (@lock) {
			calls.Clear();
			rebuildActiveSlots();
			foreach (int slotIndex in activeSlots) {
				TickerSlot slot = slots[slotIndex];
				if (slot.State != TickerSlotState.Active)
					continue;
				var now = MonoTick.GetCurrent();
				if (!slot.Scheduled.TryTakeOneIfDue(now, batchID, calls))
					continue;
				return true;
			}
			return false;
		}
	}

	private void invokeDueCall(DueTickerCall call, MonoTick budget) {
		var callbackStart = MonoTick.GetCurrent();
		TickDeadline deadline = budget > MonoTick.Zero ? new TickDeadline(callbackStart + budget) : default;

		TickCallbackTimingInfo info = call.Info;
		call.Subscription.Callback(in info, in deadline);

		var callbackEnd = MonoTick.GetCurrent();
		MonoTick runtime = callbackEnd - callbackStart;

		lock (@lock) {
			call.Subscription.LastRuntime = runtime;
			call.Subscription.InvocationCount++;
			call.Subscription.RuntimeEwma.AddSample(runtime, options.Budget.EwmaAlphaShift);
			if (deadline.HasDeadline && callbackEnd >= deadline.DeadlineAt)
				call.Subscription.OverrunCount++;
		}
	}

	private void planBudgets(List<DueTickerCall> calls, List<MonoTick> budgets) {
		budgets.Clear();
		budgets.Capacity = Math.Max(budgets.Capacity, calls.Count);

		MonoTick effectiveBudget = getEffectiveBatchBudget();
		if (effectiveBudget == MonoTick.Zero) {
			for (int i = 0; i < calls.Count; i++)
				budgets.Add(MonoTick.Zero);
			return;
		}

		UInt128 totalWeight = 0;
		for (int i = 0; i < calls.Count; i++)
			totalWeight += getSubscriptionWeight(calls[i].Subscription).Value;

		if (totalWeight == 0) {
			MonoTick equalBudget = clamp(mulDiv(effectiveBudget, 1, (ulong)calls.Count), options.Budget.MinCallbackBudget, options.Budget.MaxCallbackBudget);
			for (int i = 0; i < calls.Count; i++)
				budgets.Add(equalBudget);
			return;
		}

		for (int i = 0; i < calls.Count; i++) {
			MonoTick weight = getSubscriptionWeight(calls[i].Subscription);
			MonoTick budget = mulDiv(effectiveBudget, weight.Value, totalWeight);
			budgets.Add(clamp(budget, options.Budget.MinCallbackBudget, options.Budget.MaxCallbackBudget));
		}
	}

	private MonoTick getEffectiveBatchBudget() {
		TickerBudgetOptions budget = options.Budget;
		if (budget.TargetLoopPeriod <= budget.ReservedLoopSlack)
			return MonoTick.Zero;
		MonoTick baseBudget = budget.TargetLoopPeriod - budget.ReservedLoopSlack;
		return mulDiv(baseBudget, budget.OvercommitNumerator, budget.OvercommitDenominator);
	}

	private MonoTick getSubscriptionWeight(TickerSubscription subscription) {
		MonoTick weight = subscription.RuntimeEwma.HasValue ? subscription.RuntimeEwma.Value : options.Budget.ColdStartWeight;
		return clamp(weight, options.Budget.MinWeight, options.Budget.MaxWeight);
	}

	private static MonoTick clamp(MonoTick value, MonoTick min, MonoTick max) {
		if (value < min)
			return min;
		if (max != MonoTick.Zero && value > max)
			return max;
		return value;
	}

	private static MonoTick mulDiv(MonoTick value, ulong numerator, ulong denominator) {
		if (denominator == 0)
			throw new DivideByZeroException();
		UInt128 result = (UInt128)value.Value * numerator / denominator;
		return checked((MonoTick)(ulong)result);
	}

	private static MonoTick mulDiv(MonoTick value, ulong numerator, UInt128 denominator) {
		if (denominator == 0)
			throw new DivideByZeroException();
		UInt128 result = (UInt128)value.Value * numerator / denominator;
		return checked((MonoTick)(ulong)result);
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
			}
		);
	}
}
