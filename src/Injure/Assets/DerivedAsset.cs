// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods;

namespace Injure.Assets;

/// <summary>
/// Creates <see cref="DerivedAsset{T}"/>s.
/// </summary>
public static class DerivedAsset {
	/// <summary>
	/// Creates a value derived from one input.
	/// </summary>
	/// <typeparam name="TInput">Type of the input's value.</typeparam>
	/// <typeparam name="T">Type of the value.</typeparam>
	/// <param name="input">The input.</param>
	/// <param name="compute">Computes the value from the input's value.</param>
	/// <param name="debugName">
	/// Name for diagnostics, or <see langword="null"/> for one based on the input.
	/// </param>
	/// <returns>The derived value, owned by the caller.</returns>
	/// <inheritdoc cref="Create{T}(ReadOnlySpan{Versioned}, DeriveFunc{T}, string?)" path="/exception"/>
	public static DerivedAsset<T> Create<TInput, T>(Versioned<TInput> input, Func<TInput, T> compute, string? debugName = null)
		where TInput : class where T : class {
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(compute);
		return Create([input], ctx => compute(ctx.Get(input)), debugName);
	}

	/// <summary>
	/// Creates a value derived from two inputs.
	/// </summary>
	/// <typeparam name="TInput1">Type of the first input's value.</typeparam>
	/// <typeparam name="TInput2">Type of the second input's value.</typeparam>
	/// <typeparam name="T">Type of the value.</typeparam>
	/// <param name="input1">The first input.</param>
	/// <param name="input2">The second input.</param>
	/// <param name="compute">Computes the value from the inputs' values.</param>
	/// <param name="debugName">
	/// Name for diagnostics, or <see langword="null"/> for one based on the inputs.
	/// </param>
	/// <returns>The derived value, owned by the caller.</returns>
	/// <inheritdoc cref="Create{T}(ReadOnlySpan{Versioned}, DeriveFunc{T}, string?)" path="/exception"/>
	public static DerivedAsset<T> Create<TInput1, TInput2, T>(Versioned<TInput1> input1, Versioned<TInput2> input2, Func<TInput1, TInput2, T> compute, string? debugName = null)
		where TInput1 : class where TInput2 : class where T : class {
		ArgumentNullException.ThrowIfNull(input1);
		ArgumentNullException.ThrowIfNull(input2);
		ArgumentNullException.ThrowIfNull(compute);
		return Create([input1, input2], ctx => compute(ctx.Get(input1), ctx.Get(input2)), debugName);
	}

	/// <summary>
	/// Creates a value derived from any number of inputs.
	/// </summary>
	/// <typeparam name="T">Type of the value.</typeparam>
	/// <param name="inputs">The inputs; at least one, all from the same store.</param>
	/// <param name="compute">
	/// Computes the value; <see cref="DeriveCtx.Get{TInput}(Versioned{TInput})"/> gives the inputs'
	/// values. It doesn't have to use all of them; every input's new versions cause a recompute either
	/// way.
	/// </param>
	/// <param name="debugName">
	/// Name for diagnostics, or <see langword="null"/> for one based on the inputs.
	/// </param>
	/// <returns>The derived value, owned by the caller.</returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="compute"/> or one of <paramref name="inputs"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="inputs"/> is empty, or its inputs are from different stores.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public static DerivedAsset<T> Create<T>(ReadOnlySpan<Versioned> inputs, DeriveFunc<T> compute, string? debugName = null) where T : class {
		ArgumentNullException.ThrowIfNull(compute);
		if (inputs.IsEmpty)
			throw new ArgumentException("a derived value needs at least one input", nameof(inputs));
		foreach (Versioned d in inputs)
			ArgumentNullException.ThrowIfNull(d, nameof(inputs));
		AssetStore store = inputs[0].UntypedSlot.Store;
		foreach (Versioned d in inputs)
			if (!ReferenceEquals(d.UntypedSlot.Store, store))
				throw new ArgumentException("all inputs of a derived value must be from the same store", nameof(inputs));
		store.CheckDisposed();
		DerivedSlot<T> slot = new(store, inputs.ToArray(), compute, debugName ?? defaultName(inputs));
		store.RegisterDerived(slot);
		return new DerivedAsset<T>(slot);
	}

	private static string defaultName(ReadOnlySpan<Versioned> inputs) {
		string first = inputs[0] is IUntypedAsset a ? a.AssetId.ToString() : "derived value";
		return inputs.Length == 1 ? $"derived from {first}" : $"derived from {first} and {inputs.Length - 1} more";
	}
}

/// <summary>
/// A value computed from the current versions of other <see cref="Versioned{T}"/>s, recomputed when
/// they change.
/// </summary>
/// <typeparam name="T">Type of the value. Must be a reference type.</typeparam>
/// <remarks>
/// <para>
/// Created by
/// <see cref="DerivedAsset.Create{TInput, T}(Versioned{TInput}, Func{TInput, T}, string?)"/> and
/// its overloads. The value is computed on the first borrow or pin, and computed again on the first
/// borrow or pin after an input got a new version, on the thread doing that. Computations of one
/// derived value never run concurrently.
/// </para>
/// <para>
/// Each version of a derived value pins the input versions it was computed from, so it can
/// reference their values: they stay alive until the derived version is reclaimed. Replaced
/// versions are reclaimed exactly like replaced asset versions (see <see cref="AssetStore"/>),
/// including the custody and disposal rules of <see cref="IAssetStoreValue"/> and
/// <see cref="IAssetStoreDisposable"/>; a value implementing <see cref="IDisposable"/> but not
/// <see cref="IAssetStoreDisposable"/> makes the computation fail.
/// </para>
/// <para>
/// If computing a new version throws, the borrow or pin throws, the previous version (if any) stays
/// where it is, and the next borrow or pin tries again.
/// </para>
/// </remarks>
public sealed class DerivedAsset<T> : Versioned<T>, IDisposable where T : class {
	private readonly DerivedSlot<T> slot;

	internal DerivedAsset(DerivedSlot<T> slot) {
		this.slot = slot;
	}

	internal override VersionSlot<T> Slot => slot;

	/// <summary>
	/// Name for diagnostics.
	/// </summary>
	public string DebugName => slot.DebugName;

	/// <summary>
	/// The exception of the last failed computation, if the last computation failed.
	/// </summary>
	public ExceptionSnapshot? LastException => slot.LastException;

	/// <inheritdoc/>
	public override AssetBorrow<T> Borrow() => slot.Borrow();

	/// <inheritdoc/>
	public override AssetPin<T> Pin() => new(slot, slot.PinCurrent());

	/// <summary>
	/// Retires the current version, to be reclaimed like a replaced one, and makes further borrows and
	/// pins throw.
	/// </summary>
	public void Dispose() => slot.Dispose();
}

internal sealed class DerivedSlot<T>(AssetStore store, Versioned[] inputs, DeriveFunc<T> compute, string debugName)
	: VersionSlot<T>, AssetStore.IDerivedSlot where T : class {
	private readonly Lock @lock = new();
	private readonly Versioned[] inputs = inputs;
	private readonly DeriveFunc<T> compute = compute;
	private AssetStore.AssetVersion<T>? curr;
	private ulong nextVersion = 1;
	private bool disposed = false;
	private ExceptionSnapshot? lastException;

	public override AssetStore Store { get; } = store;
	public string DebugName { get; } = debugName;
	public ExceptionSnapshot? LastException => Volatile.Read(ref lastException);

	public override bool IsCurrent(AssetStore.AssetVersion<T> version) {
		lock (@lock)
			return !disposed && ReferenceEquals(curr, version) && !isStaleLocked();
	}

	// a version read here stays alive until this thread's next boundary, as for asset borrows
	public AssetBorrow<T> Borrow() {
		AssetThreadAttachment attachment = Store.CurrentAttachment();
		AssetStore.AssetVersion<T> v;
		lock (@lock)
			v = ensureCurrentLocked();
		return new AssetBorrow<T>(this, v, attachment);
	}

	public override AssetStore.AssetVersion<T> PinCurrent() {
		Store.CheckDisposed();
		lock (@lock) {
			AssetStore.AssetVersion<T> v = ensureCurrentLocked();
			v.AddPin();
			return v;
		}
	}

	// inputs form a DAG (they must exist before the derived value), so taking their locks
	// while holding this one can't deadlock
	private bool isStaleLocked() {
		AssetStore.AssetVersion[] inputVersions = curr!.Inputs!;
		for (int i = 0; i < inputs.Length; i++)
			if (!inputs[i].UntypedSlot.IsCurrentUntyped(inputVersions[i]))
				return true;
		return false;
	}

	private AssetStore.AssetVersion<T> ensureCurrentLocked() {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (curr is not null && !isStaleLocked())
			return curr;
		var inputVersions = new AssetStore.AssetVersion[inputs.Length];
		int pinned = 0;
		AssetStore.AssetVersion<T> newver;
		try {
			for (; pinned < inputs.Length; pinned++)
				inputVersions[pinned] = inputs[pinned].UntypedSlot.PinCurrentUntyped();
			T val = compute(new DeriveCtx(inputs, inputVersions)) ??
				throw new InvalidOperationException($"the computation of derived value '{DebugName}' returned null");
			if (val is IDisposable d && val is not IAssetStoreDisposable) {
				d.Dispose();
				throw new InvalidOperationException($"the computation of derived value '{DebugName}' returned a {val.GetType().FullName}, which implements IDisposable but not IAssetStoreDisposable");
			}
			ulong ver = nextVersion++;
			newver = new AssetStore.AssetVersion<T>(null, $"version {ver} of derived value '{DebugName}'", val, ver, ImmutableArray<IAssetDep>.Empty, inputVersions);
		} catch (Exception ex) {
			for (int i = 0; i < pinned; i++)
				inputVersions[i].ReleasePin();
			Volatile.Write(ref lastException, ExceptionSnapshot.FromException(ex));
			throw;
		}
		newver.Adopt();
		AssetStore.AssetVersion<T>? old = curr;
		curr = newver;
		Volatile.Write(ref lastException, null);
		if (old is not null)
			Store.QueueRetire(old, Store.ReserveRetireEpoch());
		return newver;
	}

	public void Dispose() {
		AssetStore.AssetVersion<T>? old;
		lock (@lock) {
			if (disposed)
				return;
			disposed = true;
			old = curr;
			curr = null;
		}
		Store.UnregisterDerived(this);
		// a disposed store already reclaimed everything, including this
		if (old is not null && !Store.IsDisposed)
			Store.QueueRetire(old, Store.ReserveRetireEpoch());
	}

	public AssetStore.AssetVersion? DetachForStoreDispose() {
		lock (@lock) {
			disposed = true;
			AssetStore.AssetVersion<T>? old = curr;
			curr = null;
			return old;
		}
	}
}
