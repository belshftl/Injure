// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

using Injure.Mods;
using Injure.Mods.CodeAnalysis;

namespace Injure.Sched.Tickers;

public sealed class TickerHandle : IReloadTeardown {
	internal readonly TickerScheduler Owner;
	internal readonly int Slot;
	internal readonly int Generation;
	private int removed = 0;

	internal TickerHandle(TickerScheduler owner, int slot, int generation) {
		if (generation <= 0)
			throw new InternalStateException("badly constructed TickerHandle; negative or 0 is not a valid generation");
		Owner = owner;
		Slot = slot;
		Generation = generation;
	}

	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public bool Remove() {
		if (Interlocked.Exchange(ref removed, 1) != 0)
			return false;
		return Owner.Remove(this);
	}

	public bool Retime(in TickerTiming timing, TickerRetimingMode mode) {
		if (Volatile.Read(ref removed) != 0)
			return false;
		return Owner.Retime(this, in timing, mode);
	}

	public TickerSubscriptionHandle Subscribe(TickerCallback callback) {
		if (Volatile.Read(ref removed) != 0)
			throw new InvalidOperationException("this ticker has already been removed from the registry");
		return Owner.Subscribe(this, callback);
	}

	public void Teardown(in ReloadTeardownContext ctx) => Remove();
}
