// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Threading;

using Injure.ModKit.Abstractions;
using Injure.ModKit.Abstractions.CodeAnalysis;

namespace Injure.Scheduling;

public sealed class TickerSubscriptionHandle : IReloadTeardown {
	private TickerScheduler? owner;
	private TickerHandle? ticker;
	private TickerSubscription? subscription;
	private int removed = 0;

	internal TickerSubscriptionHandle(TickerScheduler owner, TickerHandle ticker, TickerSubscription subscription) {
		this.owner = owner;
		this.ticker = ticker;
		this.subscription = subscription;
	}

	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public bool Remove() {
		if (Interlocked.Exchange(ref removed, 1) != 0)
			return false;
		if (owner is null || ticker is null || subscription is null)
			throw new InternalStateException("is the flag guard above broken..?");
		bool ret = owner.Unsubscribe(ticker, subscription);
		owner = null;
		ticker = null;
		subscription = null;
		return ret;
	}

	public void Teardown(in ReloadTeardownContext ctx) => Remove();
}
