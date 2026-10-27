// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions;

namespace Injure.Mods.Runtime.Tests;

public sealed class BoundedScopeTests {
	private static UntypedBoundedScopeImpl create() => new(new ReloadGeneration("test", 1), maxParallelism: 4);

	private static ValueTask invalidate(UntypedBoundedScopeImpl scope) =>
		scope.InvalidateAsync(ReloadTeardownReason.Shutdown, CancellationToken.None);

	[Fact]
	public async Task SyncAndAsyncDisposablesShareTheOrder() {
		List<string> log = new();
		UntypedBoundedScopeImpl scope = create();
		scope.AddDisp(new Logged(log, "a"));
		scope.AddAsyncDisp(new AsyncLogged(log, "b"));
		scope.AddDisp(new Logged(log, "c"));
		scope.AddAsyncDisp(new AsyncLogged(log, "d"));
		await invalidate(scope);
		Assert.Equal(["d", "c", "b", "a"], log);
	}

	[Fact]
	public async Task FailingDisposalsDontStopLaterOnes() {
		List<string> log = new();
		UntypedBoundedScopeImpl scope = create();
		scope.AddDisp(new Logged(log, "a"));
		scope.AddDisp(new Throwing());
		scope.AddDisp(new Logged(log, "b"));
		scope.AddDisp(new Throwing());
		BoundedScopeException e = await Assert.ThrowsAsync<BoundedScopeException>(() => invalidate(scope).AsTask());
		Assert.Equal(["b", "a"], log);
		Assert.Equal(2, e.Failures.Count);
	}

	[Fact]
	public async Task RegisteringAfterInvalidationThrows() {
		UntypedBoundedScopeImpl scope = create();
		await invalidate(scope);
		Assert.Throws<ReloadGenerationExpiredException>(() => scope.AddDisp(new Logged(new(), "a")));
		Assert.Throws<ReloadGenerationExpiredException>(() => scope.AddAsyncDisp(new AsyncLogged(new(), "a")));
	}

	private sealed class Logged(List<string> log, string name) : IDisposable {
		public void Dispose() => log.Add(name);
	}

	/// <summary>
	/// Delays for a bit first, so if the scope didn't properly await DisposeAsync before moving on,
	/// it'd log out of order.
	/// </summary>
	private sealed class AsyncLogged(List<string> log, string name) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Task.Delay(10);
			log.Add(name);
		}
	}

	private sealed class Throwing : IDisposable {
		public void Dispose() => throw new InvalidTimeZoneException();
	}
}
