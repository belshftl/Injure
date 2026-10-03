// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.CodeAnalysis.Internal;

namespace Injure.Mods.Abstractions;

/// <summary>
/// Lifetime-erased view of a <see cref="BoundedCt{L}"/>, for code that must handle bounded tokens
/// of any mod.
/// </summary>
[DontImplement]
public interface IUntypedBoundedCt {
	/// <summary>
	/// The generation the token belongs to.
	/// </summary>
	ReloadGeneration Generation { get; }

	/// <summary>
	/// The underlying cancellation token.
	/// </summary>
	CancellationToken Token { get; }
}

/// <summary>
/// A cancellation token that is cancelled when invalidation of its generation's scope begins.
/// </summary>
/// <typeparam name="L">Lifetime identity this cancellation token is bounded over.</typeparam>
/// <remarks>
/// <para>
/// Has an implicit case to <see cref="CancellationToken"/>, so it can be passed directly to APIs
/// that take one.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct BoundedCt<L> : IUntypedBoundedCt where L : struct, IModLifetimeIdentity {
	private readonly CancellationToken token;

	internal BoundedCt(ReloadGeneration generation, CancellationToken token) {
		if (!token.CanBeCanceled)
			throw new InternalStateException("badly constructed BoundedCt");
		Generation = generation;
		this.token = token;
	}

	/// <inheritdoc/>
	public ReloadGeneration Generation { get; }

	/// <inheritdoc/>
	public CancellationToken Token {
		get {
			if (!token.CanBeCanceled)
				throw new InvalidOperationException("this BoundedCt<L> value is uninitialized/invalid");
			return token;
		}
	}

	/// <inheritdoc cref="CancellationToken.IsCancellationRequested"/>
	public bool IsCancellationRequested => Token.IsCancellationRequested;

	/// <inheritdoc cref="CancellationToken.ThrowIfCancellationRequested"/>
	public void ThrowIfCancellationRequested() => Token.ThrowIfCancellationRequested();

	/// <inheritdoc cref="CancellationToken.Register(Action)"/>
	public CancellationTokenRegistration Register(Action callback) => Token.Register(callback);

	/// <summary>
	/// Returns the underlying <see cref="Token"/>.
	/// </summary>
	public static implicit operator CancellationToken(BoundedCt<L> token) => token.Token;
}

internal sealed class BoundedCtsCore(ReloadGeneration generation, CancellationTokenSource cts) : IDisposable {
	private readonly ReloadGeneration generation = generation;
	private readonly CancellationTokenSource cts = cts;
	private int disposed = 0;

	public ReloadGeneration Generation => generation;

	public CancellationToken Token {
		get {
			ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
			return cts.Token;
		}
	}

	public void Cancel() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		cts.Cancel();
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		try {
			cts.Cancel();
		} finally {
			cts.Dispose();
		}
	}
}

/// <summary>
/// Cancellation token source minting <see cref="BoundedCt{L}"/>.
/// </summary>
/// <typeparam name="L">Lifetime identity the minted tokens are bounded over.</typeparam>
/// <remarks>
/// Instances are created through <see cref="IBoundedScope{L}.CreateCts()"/> or
/// <see cref="IBoundedScope{L}.CreateLinkedCts(CancellationToken)"/>.
/// </remarks>
public sealed class BoundedCts<L> : IDisposable where L : struct, IModLifetimeIdentity {
	private readonly BoundedCtsCore core;
	internal BoundedCts(BoundedCtsCore core) {
		this.core = core;
	}

	/// <summary>
	/// The generation the source belongs to.
	/// </summary>
	public ReloadGeneration Generation => core.Generation;

	/// <summary>
	/// The token this source cancels.
	/// </summary>
	public BoundedCt<L> Token => new(core.Generation, core.Token);

	/// <summary>
	/// Requests cancellation of <see cref="Token"/>.
	/// </summary>
	/// <remarks>
	/// Callbacks registered on the token run synchronously on the calling thread, as with
	/// <see cref="CancellationTokenSource.Cancel()"/>.
	/// </remarks>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the source has been disposed.
	/// </exception>
	public void Cancel() => core.Cancel();

	/// <summary>
	/// Disposes of the source.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="CancellationTokenSource.Cancel()"/>, <b>this also cancels
	/// <see cref="Token"/>.</b> If you do not wish to do that, simply leave it undisposed; it will be
	/// disposed automatically by the scope.
	/// </remarks>
	public void Dispose() => core.Dispose();
}
