// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;

namespace Injure.ModKit.Abstractions;

public interface IActiveOwnerScope {
	string OwnerID { get; }
	ReloadGeneration Generation { get; }

	void AddDisposable(IDisposable disposable);
	void AddAsyncDisposable(IAsyncDisposable disposable);

	void AddOrderedDisposable(IDisposable disposable);
	void AddOrderedAsyncDisposable(IAsyncDisposable disposable);

	void Track(IReloadInvalidatable item);
	void TrackWeak(object item, string category, string description = "");
}

public interface IActiveOwnerScope<L> : IActiveOwnerScope where L : struct, IModLifetimeIdentity {
	GenerationCancellationToken<L> Stopping { get; }

	GenerationCancellationSource<L> CreateCancellationSource();
	GenerationCancellationSource<L> CreateLinkedCancellationSource(CancellationToken cancellationToken);
}

public readonly record struct ActiveOwnerScopeFailure(
	ReloadGeneration Generation,
	int Index,
	string Operation,
	string ItemTypeName,
	string ExceptionType,
	string Message,
	string Details
) {
	public static ActiveOwnerScopeFailure FromException(ReloadGeneration generation, int index, string operation, string itemTypeName, Exception ex) =>
		new(generation, index, operation, itemTypeName, ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.ToString());
}

public sealed class ActiveOwnerScopeException(
	ReloadGeneration generation,
	ReloadInvalidationReason reason,
	IReadOnlyList<ActiveOwnerScopeFailure> failures
) : Exception($"active owner scope invalidation failed for '{generation}' with {failures.Count} failure(s)") {
	public ReloadGeneration Generation { get; } = generation;
	public ReloadInvalidationReason Reason { get; } = reason;
	public IReadOnlyList<ActiveOwnerScopeFailure> Failures { get; } = failures;
}
