// SPDX-License-Identifier: MIT

using System;
using System.Threading;

using Injure.Internals.Analyzers.Attributes;

namespace Injure.ModKit.Abstractions;

public readonly record struct ReloadGeneration(string OwnerID, ulong Value) {
	public override string ToString() => $"{OwnerID}@{Value:D4}";
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ReloadInvalidationReason {
	public enum Case {
		Reload = 1,
		Disable,
		Shutdown,
		FailureRollback,
		PartialReload
	}
}

public sealed class ReloadInvalidationContext {
	public required string OwnerID { get; init; }
	public required ReloadGeneration OldGeneration { get; init; }
	public required ReloadInvalidationReason Reason { get; init; }
}

public interface IReloadInvalidatable {
	void Invalidate(ReloadInvalidationContext ctx);
}

public sealed class ReloadGenerationExpiredException(ReloadGeneration? gen) : InvalidOperationException($"object belongs to expired reload generation {gen?.ToString() ?? "<unknown>"}") {
	public ReloadGeneration? Generation { get; } = gen;
}

public abstract class ReloadBoundObject : IReloadInvalidatable {
	private readonly ReloadGeneration? generation;
	private int invalidated = 0;
	public bool IsInvalidated => Volatile.Read(ref invalidated) != 0;

	protected ReloadBoundObject() {
	}

	protected ReloadBoundObject(ReloadGeneration generation) {
		this.generation = generation;
	}

	public void Invalidate(ReloadInvalidationContext ctx) {
		if (Interlocked.Exchange(ref invalidated, 1) != 0)
			return;
		OnInvalidated(ctx);
	}

	protected void ThrowIfInvalidated() {
		if (!IsInvalidated)
			return;
		throw new ReloadGenerationExpiredException(generation);
	}

	protected virtual void OnInvalidated(ReloadInvalidationContext ctx) {
	}
}
