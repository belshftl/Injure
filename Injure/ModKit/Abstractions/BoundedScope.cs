// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;

using Injure.ModKit.Abstractions.CodeAnalysis;

namespace Injure.ModKit.Abstractions;

public readonly record struct ReloadWeakReferenceSnapshot(
	ReloadGeneration Generation,
	string Category,
	string Description,
	bool IsAlive,
	string TargetTypeName
);

public readonly record struct BoundedScopeFailure(
	ReloadGeneration Generation,
	int Index,
	string Operation,
	string ItemTypeName,
	ExceptionSnapshot Exception
) {
	public static BoundedScopeFailure FromException(ReloadGeneration generation, int index, string operation, string itemTypeName, Exception ex) =>
		new(generation, index, operation, itemTypeName, ExceptionSnapshot.FromException(ex));
}

public sealed class BoundedScopeException(
	ReloadGeneration generation,
	ReloadTeardownReason reason,
	IReadOnlyList<BoundedScopeFailure> failures
) : Exception($"active owner scope invalidation failed for '{generation}' with {failures.Count} failure(s)") {
	public ReloadGeneration Generation { get; } = generation;
	public ReloadTeardownReason Reason { get; } = reason;
	public IReadOnlyList<BoundedScopeFailure> Failures { get; } = failures;
}

/// <summary>
/// Represents a runtime-managed disposal scope bounded over a type-erased lifetime identity.
/// </summary>
/// <remarks>
/// <para>
/// This is the lifetime-identity-erased form of <see cref="IBoundedScope{L}"/>; prefer
/// <see cref="IBoundedScope{L}"/> in most scenarios. This interface is intended for
/// APIs that must work with scopes belonging to arbitrary mod generations.
/// </para>
/// <para>
/// The order in which the disposals/teardowns are performed is as follows:
/// <list type="bullet">
/// <item><description>First, all <see cref="IReloadTeardown"/>s are torn down in parallel.</description></item>
/// <item><description>Second, all parallel disposals are performed.</description></item>
/// <item><description>Finally, all ordered disposals are performed in reverse registration order.</description></item>
/// </list>
/// </para>
/// <para>
/// A thrown exception by a teardown/dispose method is caught and recorded without
/// interrupting the operation, subject to the standard exception-recording rules;
/// see <c>Docs/exception-recording.md</c> for more info. At the end of the scope invalidation,
/// if any exceptions occurred, a <see cref="BoundedScopeException"/> is thrown containing
/// the failure records and <see cref="ExceptionSnapshot"/>s.
/// </para>
/// <para>
/// All members are guaranteed to be thread-safe, including while the scope is concurrently
/// being invalidated. A registration either successfully performs the addition to the scope or
/// fails with an exception; a registration is never reported as successful and then omitted
/// from teardown or disposal.
/// </para>
/// <para>
/// The maximum degree of parallelism for <see cref="IReloadTeardown"/> teardown or
/// parallel disposals is configured by the runtime.
/// </para>
/// </remarks>
public interface IUntypedBoundedScope : IParallelDisposalScope {
	/// <summary>
	/// Generation that this scope is bounded over. This property remains available
	/// after scope invalidation has already begun or finished.
	/// </summary>
	ReloadGeneration Generation { get; }

	/// <summary>
	/// Registers an <see cref="IReloadTeardown"/> for unordered, parallel teardown
	/// when this reload generation ends.
	/// </summary>
	/// <typeparam name="T">
	/// The teardown type.
	/// </typeparam>
	/// <param name="teardown">
	/// The teardown registration to add.
	/// </param>
	/// <returns>
	/// <paramref name="teardown"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <remarks>
	/// No ordered registration/teardown API exists for <see cref="IReloadTeardown"/>, as
	/// idempotence, lack of ordering dependencies, and thread safety are contracts of the
	/// interface, thus making an ordered teardown API superfluous.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="teardown"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ReloadGenerationExpiredException">
	/// Thrown if invalidation of this scope has already begun or finished.
	/// </exception>
	[SatisfiesAndReturnsObligation(nameof(teardown), ObligationSatisfactionLevel.Generation)]
	T AddTeardown<T>(T teardown) where T : notnull, IReloadTeardown;

	/// <summary>
	/// Registers a synchronous resource for unordered, parallel disposal when
	/// this reload generation ends.
	/// </summary>
	/// <typeparam name="T">
	/// The type of the resource.
	/// </typeparam>
	/// <param name="disposable">
	/// The resource to register.
	/// </param>
	/// <returns>
	/// <paramref name="disposable"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ReloadGenerationExpiredException">
	/// Thrown if invalidation of this scope has already begun or finished.
	/// </exception>
	[SatisfiesAndReturnsObligation(nameof(disposable), ObligationSatisfactionLevel.Generation)]
	new T AddDisposable<T>(T disposable) where T : notnull, IDisposable;

	/// <summary>
	/// Registers an asynchronous resource for unordered, parallel disposal when
	/// this reload generation ends.
	/// </summary>
	/// <typeparam name="T">
	/// The type of the resource.
	/// </typeparam>
	/// <param name="disposable">
	/// The resource to register.
	/// </param>
	/// <returns>
	/// <paramref name="disposable"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ReloadGenerationExpiredException">
	/// Thrown if invalidation of this scope has already begun or finished.
	/// </exception>
	[SatisfiesAndReturnsObligation(nameof(disposable), ObligationSatisfactionLevel.Generation)]
	new T AddAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable;

	/// <summary>
	/// Registers a synchronous resource for ordered disposal in reverse registration
	/// order when this reload generation ends.
	/// </summary>
	/// <typeparam name="T">
	/// The type of the resource.
	/// </typeparam>
	/// <param name="disposable">
	/// The resource to register.
	/// </param>
	/// <returns>
	/// <paramref name="disposable"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ReloadGenerationExpiredException">
	/// Thrown if invalidation of this scope has already begun or finished.
	/// </exception>
	[SatisfiesAndReturnsObligation(nameof(disposable), ObligationSatisfactionLevel.Generation)]
	new T AddOrderedDisposable<T>(T disposable) where T : notnull, IDisposable;

	/// <summary>
	/// Registers an asynchronous resource for ordered disposal in reverse registration
	/// order when this reload generation ends.
	/// </summary>
	/// <typeparam name="T">
	/// The type of the resource.
	/// </typeparam>
	/// <param name="disposable">
	/// The resource to register.
	/// </param>
	/// <returns>
	/// <paramref name="disposable"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <remarks>
	/// The <see cref="IAsyncDisposable.DisposeAsync()"/> call is awaited before moving
	/// on to the next one; no concurrency is performed by the scope.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ReloadGenerationExpiredException">
	/// Thrown if invalidation of this scope has already begun or finished.
	/// </exception>
	[SatisfiesAndReturnsObligation(nameof(disposable), ObligationSatisfactionLevel.Generation)]
	new T AddOrderedAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable;

	/// <summary>
	/// Tracks an object weakly for diagnostics related to unloading this reload generation.
	/// </summary>
	/// <param name="item">
	/// The object to track. The scope does not retain a strong reference to it.
	/// </param>
	/// <param name="category">
	/// A short category identifying the kind of tracked object.
	/// </param>
	/// <param name="description">
	/// Optional human-readable diagnostic context describing the object or why it is
	/// expected to become unreachable.
	/// </param>
	/// <remarks>
	/// Used for diagnostics. All weak references that remain alive may be reported in
	/// the runtime's mod unload diagnostics. Registering the same object twice creates
	/// independent tracking entries; no deduplication or coalescing is attempted.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="item"/>, <paramref name="category"/>, or
	/// <paramref name="description"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="category"/> is empty or consists solely of whitespace.
	/// </exception>
	/// <exception cref="ReloadGenerationExpiredException">
	/// Thrown if invalidation of this scope has already begun or finished.
	/// </exception>
	void TrackWeak(object item, string category, string description = "");
}

/// <summary>
/// Represents a runtime-managed disposal scope bounded over <typeparamref name="L"/>.
/// </summary>
/// <typeparam name="L">
/// Lifetime identity bound or specifier; see <c>Docs/mods/lifetime-identity.md</c>
/// for more info.
/// </typeparam>
/// <remarks>
/// <para>
/// The order in which the disposals/teardowns are performed is as follows:
/// <list type="bullet">
/// <item><description>First, all <see cref="IReloadTeardown"/>s are torn down in parallel.</description></item>
/// <item><description>Second, all parallel disposals are performed.</description></item>
/// <item><description>Finally, all ordered disposals are performed in reverse registration order.</description></item>
/// </list>
/// </para>
/// <para>
/// A thrown exception by a teardown/dispose method is caught and recorded without
/// interrupting the operation, subject to the standard exception-recording rules;
/// see <c>Docs/exception-recording.md</c> for more info. At the end of the scope invalidation,
/// if any exceptions occurred, a <see cref="BoundedScopeException"/> is thrown containing
/// the failure records and <see cref="ExceptionSnapshot"/>s.
/// </para>
/// <para>
/// All members are guaranteed to be thread-safe, including while the scope is concurrently
/// being invalidated. A registration either successfully performs the addition to the scope or
/// fails with an exception; a registration is never reported as successful and then omitted
/// from teardown or disposal.
/// </para>
/// <para>
/// The maximum degree of parallelism for <see cref="IReloadTeardown"/> teardown or
/// parallel disposals is configured by the runtime.
/// </para>
/// </remarks>
public interface IBoundedScope<L> : IUntypedBoundedScope where L : struct, IModLifetimeIdentity {
	/// <summary>
	/// A cancellation token that fires when scope invalidation begins. This
	/// property remains available after scope invalidation has already begun
	/// or finished.
	/// </summary>
	BoundedCt<L> Stopping { get; }

	/// <summary>
	/// Creates a cancellation token source minting tokens bounded over <typeparamref name="L"/>.
	/// </summary>
	/// <exception cref="ReloadGenerationExpiredException">
	/// Thrown if invalidation of this scope has already begun or finished.
	/// </exception>
	BoundedCts<L> CreateCts();

	/// <summary>
	/// Creates a cancellation token source linked to an existing <see cref="CancellationToken"/>
	/// minting tokens bounded over <typeparamref name="L"/>.
	/// </summary>
	/// <exception cref="ReloadGenerationExpiredException">
	/// Thrown if invalidation of this scope has already begun or finished.
	/// </exception>
	BoundedCts<L> CreateLinkedCts(CancellationToken link);
}
