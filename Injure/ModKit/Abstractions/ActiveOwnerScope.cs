// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Injure.ModKit.Abstractions;

public interface IActiveOwnerScope {
	string OwnerID { get; }
	ReloadGeneration Generation { get; }

	void AddTeardown(IReloadTeardown teardown);

	void AddDisposable(IDisposable disposable);
	void AddAsyncDisposable(IAsyncDisposable disposable);

	void AddOrderedDisposable(IDisposable disposable);
	void AddOrderedAsyncDisposable(IAsyncDisposable disposable);

	void TrackWeak(object item, string category, string description = "");
}

public interface IActiveOwnerScope<L> : IActiveOwnerScope where L : struct, IModLifetimeIdentity {
	BoundedCt<L> Stopping { get; }

	BoundedCts<L> CreateCts();
	BoundedCts<L> CreateLinkedCts(CancellationToken link);
}
