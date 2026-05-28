// SPDX-License-Identifier: MIT

using System;
using System.Threading;

using Injure.ModKit.Abstractions.CodeAnalysis;

namespace Injure.ModKit.Abstractions;

public interface IActiveOwnerScope {
	string OwnerID { get; }
	ReloadGeneration Generation { get; }

	[SatisfiesAndReturns(nameof(teardown))] T AddTeardown<T>(T teardown) where T : notnull, IReloadTeardown;

	[SatisfiesAndReturns(nameof(disposable))] T AddDisposable<T>(T disposable) where T : notnull, IDisposable;
	[SatisfiesAndReturns(nameof(disposable))] T AddAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable;

	[SatisfiesAndReturns(nameof(disposable))] T AddOrderedDisposable<T>(T disposable) where T : notnull, IDisposable;
	[SatisfiesAndReturns(nameof(disposable))] T AddOrderedAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable;

	void TrackWeak(object item, string category, string description = "");
}

public interface IActiveOwnerScope<L> : IActiveOwnerScope where L : struct, IModLifetimeIdentity {
	BoundedCt<L> Stopping { get; }

	BoundedCts<L> CreateCts();
	BoundedCts<L> CreateLinkedCts(CancellationToken link);
}
