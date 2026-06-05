// SPDX-License-Identifier: MIT

using System;
using System.Threading;

using Injure.ModKit.Abstractions.CodeAnalysis;

namespace Injure.ModKit.Abstractions;

public interface IUntypedBoundedScope {
	string OwnerID { get; }
	ReloadGeneration Generation { get; }

	[SatisfiesAndReturnsObligation(nameof(teardown), ObligationSatisfactionLevel.Generation)]
	T AddTeardown<T>(T teardown) where T : notnull, IReloadTeardown;

	[SatisfiesAndReturnsObligation(nameof(disposable), ObligationSatisfactionLevel.Generation)]
	T AddDisposable<T>(T disposable) where T : notnull, IDisposable;

	[SatisfiesAndReturnsObligation(nameof(disposable), ObligationSatisfactionLevel.Generation)]
	T AddAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable;

	[SatisfiesAndReturnsObligation(nameof(disposable), ObligationSatisfactionLevel.Generation)]
	T AddOrderedDisposable<T>(T disposable) where T : notnull, IDisposable;

	[SatisfiesAndReturnsObligation(nameof(disposable), ObligationSatisfactionLevel.Generation)]
	T AddOrderedAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable;

	void TrackWeak(object item, string category, string description = "");
}

public interface IBoundedScope<L> : IUntypedBoundedScope where L : struct, IModLifetimeIdentity {
	BoundedCt<L> Stopping { get; }

	BoundedCts<L> CreateCts();
	BoundedCts<L> CreateLinkedCts(CancellationToken link);
}
