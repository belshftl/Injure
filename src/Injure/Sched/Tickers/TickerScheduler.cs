// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Injure.Mods.CodeAnalysis;
using Injure.Host;

namespace Injure.Sched.Tickers;

internal struct EwmaDuration {
	private long valNs;
	private bool initialized;

	public readonly bool HasValue => initialized;
	public readonly HostDuration Value => HostDuration.FromNs(valNs);

	public void AddSample(HostDuration sample, int alphaShift) {
		ArgumentOutOfRangeException.ThrowIfNegative(alphaShift);

		long sampleNs = sample.Ns;
		if (!initialized) {
			valNs = sampleNs;
			initialized = true;
			return;
		}

		if (sampleNs >= valNs)
			valNs += sampleNs - valNs >> alphaShift;
		else
			valNs -= valNs - sampleNs >> alphaShift;
	}
}

internal sealed class TickerSubscription(TickerCallback callback) {
	public TickerCallback Callback { get; } = callback;
	public EwmaDuration RuntimeEwma;
	public HostDuration LastRuntime;
	public ulong InvocationCount;
	public ulong OverrunCount;
}

internal readonly record struct DueTickerCall(TickerSubscription Subscription, TickCallbackTimingInfo Info);

internal sealed class ScheduledTicker {
	private readonly TickerOptions options;
	private readonly List<TickerSubscription> subscriptions = new();
	private TickerTiming timing;

	private bool hadCallback;
	// only meaningful when hadCallback is set
	private HostTick lastScheduledAt;
	private HostTick lastActualAt;
	private uint lastBatchID;
	private int runsThisBatch;

	public HostTick NextAt { get; private set; } // only meaningful once activated
	public int Priority => options.Priority;
	public ulong InsertionOrder { get; private set; } // tie breaker for deterministic sorting of equal ones

	public ScheduledTicker(in TickerSpec spec) {
		validateTiming(spec.Timing, nameof(spec));
		if (spec.Options.OverrunMode == TickerOverrunMode.CatchUp)
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.Options.MaxBurst);
		options = spec.Options;
		timing = spec.Timing;

		hadCallback = false;
		lastBatchID = 0;
		runsThisBatch = 0;
		InsertionOrder = 0;
	}

	public void Activate(HostTick commitAt, ulong insertionOrder) {
		hadCallback = false;
		lastBatchID = 0;
		runsThisBatch = 0;
		NextAt = options.StartMode.Tag switch {
			TickerStartMode.Case.FromCommitTime => commitAt + timing.InitialOffset,
			TickerStartMode.Case.AtAbsoluteTick => options.StartAt,
			_ => throw new UnreachableException(),
		};
		InsertionOrder = insertionOrder;
	}

	public void Retime(HostTick commitAt, in TickerTiming tm, TickerRetimingMode mode) {
		validateTiming(tm, nameof(tm));

		HostDuration oldPeriod = timing.Period;
		HostTick oldNextAt = NextAt;
		timing = tm;
		switch (mode.Tag) {
		case TickerRetimingMode.Case.KeepPhase:
			hadCallback = false;
			lastBatchID = 0;
			runsThisBatch = 0;
			NextAt = commitAt + timing.InitialOffset;
			break;
		case TickerRetimingMode.Case.RestartFromCommitTime:
			if (oldPeriod <= HostDuration.Zero)
				throw new InternalStateException("oldPeriod is somehow not positive, this should've been rejected earlier");
			if (oldNextAt > commitAt) {
				HostDuration rem = oldNextAt - commitAt;
				Int128 newrem128 = (Int128)rem.Ns * timing.Period.Ns / oldPeriod.Ns;
				NextAt = commitAt + HostDuration.FromNs(checked((long)newrem128));
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

	public bool TryTakeOneIfDue(HostTick now, uint batchID, List<DueTickerCall> calls) {
		if (now < NextAt)
			return false;

		HostTick scheduledAt;
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
			long missed = (now - scheduledAt) / timing.Period;
			NextAt = scheduledAt + timing.Period * checked(missed + 1);
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

	private TickCallbackTimingInfo makeInfo(HostTick scheduledAt, HostTick actualAt) {
		HostTick previousScheduledAt = hadCallback ? lastScheduledAt : subtractSaturating(scheduledAt, timing.Period);
		HostTick previousActualAt = hadCallback ? lastActualAt : subtractSaturating(actualAt, timing.Period);
		HostDuration elapsed = hadCallback ? actualAt - lastActualAt : timing.Period;
		HostDuration late = actualAt >= scheduledAt ? actualAt - scheduledAt : HostDuration.Zero;

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

	private void markCallbackState(HostTick scheduledAt, HostTick actualAt) {
		lastScheduledAt = scheduledAt;
		lastActualAt = actualAt;
		hadCallback = true;
	}

	// the first callback reports one period before itself as "previous", which can be before the
	// clock's epoch if the ticker starts right after it
	private static HostTick subtractSaturating(HostTick tick, HostDuration period) =>
		tick - HostTick.Epoch >= period ? tick - period : HostTick.Epoch;

	private static void validateTiming(in TickerTiming tm, string paramName) {
		if (tm.Period <= HostDuration.Zero)
			throw new ArgumentOutOfRangeException(paramName, "period must be positive");
		if (tm.InitialOffset < HostDuration.Zero)
			throw new ArgumentOutOfRangeException(paramName, "initial offset must not be negative");
	}
}

public readonly record struct TickerBudgetOptions(
	HostDuration TargetLoopPeriod,
	HostDuration ReservedLoopSlack,
	uint OvercommitNumerator,
	uint OvercommitDenominator,
	HostDuration ColdStartWeight,
	HostDuration MinWeight,
	HostDuration MaxWeight,
	HostDuration MinCallbackBudget,
	HostDuration MaxCallbackBudget,
	int EwmaAlphaShift
) {
	public static TickerBudgetOptions CreateDefault(HostDuration targetLoopPeriod, HostDuration? reservedLoopSlack = null) {
		static HostDuration defaultReservedSlack(HostDuration targetLoopPeriod) {
			// reserve ~10% of the loop period clamped to [1/64, 1/4]
			HostDuration slack = targetLoopPeriod / 10;
			HostDuration min = maxOne(targetLoopPeriod / 64);
			HostDuration max = targetLoopPeriod / 4;
			if (slack < min)
				return min;
			if (slack > max)
				return max;
			return slack;
		}
		static HostDuration maxOne(HostDuration value) => value == HostDuration.Zero ? HostDuration.FromNs(1) : value;

		if (targetLoopPeriod <= HostDuration.Zero)
			throw new ArgumentOutOfRangeException(nameof(targetLoopPeriod), "target loop period must be positive");
		HostDuration slack = reservedLoopSlack ?? defaultReservedSlack(targetLoopPeriod);
		if (slack < HostDuration.Zero)
			throw new ArgumentOutOfRangeException(nameof(reservedLoopSlack), "reserved loop slack must not be negative");
		if (slack > targetLoopPeriod / 2) // keep slack sane, at most 1/2 of the full loop
			slack = targetLoopPeriod / 2;
		return new TickerBudgetOptions(
			TargetLoopPeriod: targetLoopPeriod,
			ReservedLoopSlack: slack,
			OvercommitNumerator: 8,
			OvercommitDenominator: 1,
			ColdStartWeight: targetLoopPeriod / 64,
			MinWeight: maxOne(targetLoopPeriod / 4096),
			MaxWeight: targetLoopPeriod,
			MinCallbackBudget: maxOne(targetLoopPeriod / 1024),
			MaxCallbackBudget: targetLoopPeriod,
			EwmaAlphaShift: 4
		);
	}

	public static readonly TickerBudgetOptions Default480Hz = CreateDefault(HostDuration.PeriodFromHz(480.0));

	internal TickerBudgetOptions Normalize() {
		TickerBudgetOptions options = Equals(default) ? Default480Hz : this;
		if (options.OvercommitDenominator == 0)
			options = options with { OvercommitDenominator = 1 };
		if (options.OvercommitNumerator == 0)
			options = options with { OvercommitNumerator = 1 };
		if (options.EwmaAlphaShift < 0)
			options = options with { EwmaAlphaShift = 0 };
		if (options.MinWeight <= HostDuration.Zero)
			options = options with { MinWeight = HostDuration.FromNs(1) };
		if (options.MaxWeight != HostDuration.Zero && options.MaxWeight < options.MinWeight)
			options = options with { MaxWeight = options.MinWeight };
		if (options.MinCallbackBudget < HostDuration.Zero)
			options = options with { MinCallbackBudget = HostDuration.Zero };
		if (options.MaxCallbackBudget != HostDuration.Zero && options.MaxCallbackBudget < options.MinCallbackBudget)
			options = options with { MaxCallbackBudget = options.MinCallbackBudget };
		return options;
	}
}

public readonly record struct TickerSchedulerOptions(
	int BatchCallLimit = 64,
	int EventPollInterval = 8,
	HostDuration MaxBatchDuration = default,
	TickerBudgetOptions Budget = default
);

public sealed class TickerScheduler(IHostClock clock, in TickerSchedulerOptions options) : ITickerRegistry {
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

	private readonly IHostClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
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
			HostTick commitAt = clock.Now;
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
		HostTick start = clock.Now;
		List<DueTickerCall> dueCalls = new();
		List<HostDuration> dueBudgets = new();

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
			if (options.MaxBatchDuration > HostDuration.Zero) {
				HostDuration elapsed = clock.Now - start;
				if (elapsed >= options.MaxBatchDuration)
					return;
			}
		}
	}

	public bool TryGetEarliestNextAt(out HostTick nextAt) {
		lock (@lock) {
			if (activeSlots.Count == 0) {
				nextAt = default;
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
				HostTick now = clock.Now;
				if (!slot.Scheduled.TryTakeOneIfDue(now, batchID, calls))
					continue;
				return true;
			}
			return false;
		}
	}

	private void invokeDueCall(DueTickerCall call, HostDuration budget) {
		HostTick callbackStart = clock.Now;
		TickDeadline deadline = budget > HostDuration.Zero ? new TickDeadline(clock, callbackStart + budget) : default;

		TickCallbackTimingInfo info = call.Info;
		call.Subscription.Callback(in info, in deadline);

		HostTick callbackEnd = clock.Now;
		HostDuration runtime = callbackEnd - callbackStart;

		lock (@lock) {
			call.Subscription.LastRuntime = runtime;
			call.Subscription.InvocationCount++;
			call.Subscription.RuntimeEwma.AddSample(runtime, options.Budget.EwmaAlphaShift);
			if (deadline.HasDeadline && callbackEnd >= deadline.DeadlineAt)
				call.Subscription.OverrunCount++;
		}
	}

	private void planBudgets(List<DueTickerCall> calls, List<HostDuration> budgets) {
		budgets.Clear();
		budgets.Capacity = Math.Max(budgets.Capacity, calls.Count);

		HostDuration effectiveBudget = getEffectiveBatchBudget();
		if (effectiveBudget == HostDuration.Zero) {
			for (int i = 0; i < calls.Count; i++)
				budgets.Add(HostDuration.Zero);
			return;
		}

		UInt128 totalWeight = 0;
		for (int i = 0; i < calls.Count; i++)
			totalWeight += nonNegativeNs(getSubscriptionWeight(calls[i].Subscription));

		if (totalWeight == 0) {
			HostDuration equalBudget = clamp(mulDiv(effectiveBudget, 1, (ulong)calls.Count), options.Budget.MinCallbackBudget, options.Budget.MaxCallbackBudget);
			for (int i = 0; i < calls.Count; i++)
				budgets.Add(equalBudget);
			return;
		}

		for (int i = 0; i < calls.Count; i++) {
			HostDuration weight = getSubscriptionWeight(calls[i].Subscription);
			HostDuration budget = mulDiv(effectiveBudget, nonNegativeNs(weight), totalWeight);
			budgets.Add(clamp(budget, options.Budget.MinCallbackBudget, options.Budget.MaxCallbackBudget));
		}
	}

	private HostDuration getEffectiveBatchBudget() {
		TickerBudgetOptions budget = options.Budget;
		if (budget.TargetLoopPeriod <= budget.ReservedLoopSlack)
			return HostDuration.Zero;
		HostDuration baseBudget = budget.TargetLoopPeriod - budget.ReservedLoopSlack;
		return mulDiv(baseBudget, budget.OvercommitNumerator, budget.OvercommitDenominator);
	}

	private HostDuration getSubscriptionWeight(TickerSubscription subscription) {
		HostDuration weight = subscription.RuntimeEwma.HasValue ? subscription.RuntimeEwma.Value : options.Budget.ColdStartWeight;
		return clamp(weight, options.Budget.MinWeight, options.Budget.MaxWeight);
	}

	private static HostDuration clamp(HostDuration value, HostDuration min, HostDuration max) {
		if (value < min)
			return min;
		if (max != HostDuration.Zero && value > max)
			return max;
		return value;
	}

	// budget math only ever deals with non-negative durations
	private static ulong nonNegativeNs(HostDuration value) {
		Debug.Assert(value >= HostDuration.Zero, "budget math got a negative duration");
		return (ulong)value.Ns;
	}

	private static HostDuration mulDiv(HostDuration value, ulong numerator, UInt128 denominator) {
		if (denominator == 0)
			throw new DivideByZeroException();
		UInt128 result = (UInt128)nonNegativeNs(value) * numerator / denominator;
		return HostDuration.FromNs(checked((long)(ulong)result));
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
