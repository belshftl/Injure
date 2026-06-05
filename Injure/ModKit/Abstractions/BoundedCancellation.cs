// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Injure.ModKit.Abstractions;

public interface IUntypedBoundedCt {
	ReloadGeneration Generation { get; }
	CancellationToken Token { get; }
}

public readonly struct BoundedCt<L> : IUntypedBoundedCt where L : struct, IModLifetimeIdentity {
	private readonly CancellationToken token;
	private readonly int inited;

	internal BoundedCt(ReloadGeneration generation, CancellationToken token) {
		if (!token.CanBeCanceled)
			throw new InternalStateException("badly constructed BoundedCt");
		Generation = generation;
		this.token = token;
		inited = 1;
	}

	public ReloadGeneration Generation { get; }

	public CancellationToken Token {
		get {
			if (inited == 0)
				throw new InvalidOperationException("this BoundedCt was not initialized (did you accidentally pass `default`?)");
			return token;
		}
	}

	public bool IsCancellationRequested => Token.IsCancellationRequested;

	public void ThrowIfCancellationRequested() => Token.ThrowIfCancellationRequested();

	public CancellationTokenRegistration Register(Action callback) => Token.Register(callback);

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
		if (Volatile.Read(ref disposed) != 0)
			return;
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

public sealed class BoundedCts<L> : IDisposable where L : struct, IModLifetimeIdentity {
	private readonly BoundedCtsCore core;
	internal BoundedCts(BoundedCtsCore core) {
		this.core = core;
	}
	public ReloadGeneration Generation => core.Generation;
	public BoundedCt<L> Token => new(core.Generation, core.Token);
	public void Cancel() => core.Cancel();
	public void Dispose() => core.Dispose();
}
