// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Injure.ModKit.Abstractions;

public interface IGenerationCancellationToken {
	ReloadGeneration Generation { get; }
	CancellationToken Token { get; }
}

public readonly struct GenerationCancellationToken<L> : IGenerationCancellationToken where L : struct, IModLifetimeIdentity {
	private readonly CancellationToken token;
	private readonly int inited;

	internal GenerationCancellationToken(ReloadGeneration generation, CancellationToken token) {
		if (!token.CanBeCanceled)
			throw new InternalStateException("badly constructed GenerationCancellationToken");
		Generation = generation;
		this.token = token;
		inited = 1;
	}

	public ReloadGeneration Generation { get; }

	public CancellationToken Token {
		get {
			if (inited == 0)
				throw new InvalidOperationException("this GenerationCancellationToken was not initialized (did you accidentally pass `default`?)");
			return token;
		}
	}

	public bool IsCancellationRequested => Token.IsCancellationRequested;

	public void ThrowIfCancellationRequested() => Token.ThrowIfCancellationRequested();

	public CancellationTokenRegistration Register(Action callback) => Token.Register(callback);

	public static implicit operator CancellationToken(GenerationCancellationToken<L> token) => token.Token;
}

internal sealed class GenerationCancellationSourceCore(ReloadGeneration generation, CancellationTokenSource cts) : IDisposable {
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

public sealed class GenerationCancellationSource<L> : IDisposable where L : struct, IModLifetimeIdentity {
	private readonly GenerationCancellationSourceCore core;
	internal GenerationCancellationSource(GenerationCancellationSourceCore core) {
		this.core = core;
	}
	public ReloadGeneration Generation => core.Generation;
	public GenerationCancellationToken<L> Token => new(core.Generation, core.Token);
	public void Cancel() => core.Cancel();
	public void Dispose() => core.Dispose();
}
