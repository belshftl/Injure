// SPDX-License-Identifier: MIT

using System.Threading;

using Injure.ModKit.Abstractions;

namespace Injure.Scheduling;

public sealed class TickerSubscriptionHandle : IReloadTeardown {
	private TickerScheduler? owner;
	private TickerHandle? ticker;
	private TickerCallback? callback;
	private int removed = 0;

	internal TickerSubscriptionHandle(TickerScheduler owner, TickerHandle ticker, TickerCallback callback) {
		this.owner = owner;
		this.ticker = ticker;
		this.callback = callback;
	}

	public bool Remove() {
		if (Interlocked.Exchange(ref removed, 1) != 0)
			return false;
		if (owner is null || ticker is null || callback is null)
			throw new InternalStateException("is the flag guard above broken..?");
		bool ret = owner.Unsubscribe(ticker, callback);
		owner = null;
		ticker = null;
		callback = null;
		return ret;
	}

	public void Teardown(in ReloadTeardownContext ctx) => Remove();
}
