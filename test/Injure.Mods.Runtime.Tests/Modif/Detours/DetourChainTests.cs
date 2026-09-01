// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.CompilerServices;
using Injure.Mods.Runtime.Modif.Detours;

namespace Injure.Mods.Runtime.Tests.Modif.Detours;

/// <remarks>
/// Invoking a chain directly through the head rather than a patched method works for tests because
/// the terminus calls the target normally, and with no prologue installed, the bypass token is left
/// unconsumed and is unwound by the terminus's <c>finally</c>. This makes tests viable without a
/// profiler.
/// </remarks>
public sealed class DetourChainTests {
	private delegate int NextCompute(int value);
	private delegate int NextComputeWidened(object value);
	private delegate void NextRecord(string text);
	private delegate int NextScale(Accumulator instance, int value);
	private delegate int NextScaleWidened(object instance, int value);
	private delegate int NextRef(ref int value);

	private sealed class Payload {
		public int Slot { get; init; }
	}

	private sealed class Accumulator {
		public int Factor { get; init; } = 3;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Scale(int value) => value * Factor;
	}

	private static List<string> log { get; } = new();

	// targets
	[MethodImpl(MethodImplOptions.NoInlining)] private static int compute(int value) => value + 1;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void record(string text) => log.Add(text);
	[MethodImpl(MethodImplOptions.NoInlining)] private static int unwrap(Payload payload) => payload.Slot;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int bump(ref int value) => ++value;

	// detour impls
	private static int doubles(NextCompute next, int value) => next(value) * 2;
	private static int addsTen(NextCompute next, int value) => next(value) + 10;
#pragma warning disable IDE0060 // remove unused parameter
	private static int bypasses(NextCompute next, int value) => 99;
#pragma warning restore IDE0060 // remove unused parameter
	private static void prefixes(NextRecord next, string text) => next($"[{text}]");
	private static int offsets(NextScale next, Accumulator instance, int value) =>
		next(instance, value) + 1;
	private static int increments(NextRef next, ref int value) => next(ref value) + 100;
	private static int widensInt(NextComputeWidened next, object value) => next(value) * 2;
	private static int widensPayload(Func<object, int> next, object payload) => next(payload) + 1;
	private static int widensWithInstance(NextScaleWidened next, object instance, int value) =>
		next(instance, value) + 1;
#pragma warning disable IDE0060 // remove unused parameter
	private static int passesWrongType(Func<object, int> next, object payload) => next("string");
	private static int passesNull(NextComputeWidened next, object value) => next(null!);
#pragma warning restore IDE0060 // remove unused parameter

	// bad detour impls
	private static int wrongParameterCount(NextCompute next) => next(0);
	private static int firstParamNotDelegate(int notNext, int value) => notNext + value;
	private static string wrongReturn(NextCompute next, int value) => next(value).ToString();
	private static int narrowedParameter(Func<string, int> next, string value) => next(value);
	private static object widenedReturn(Func<Payload, object> next, Payload payload) => next(payload);
#pragma warning disable IDE0060 // remove unused parameter
	private static int mismatchedNext(Func<int, int> next, object value) => 0;
#pragma warning restore IDE0060 // remove unused parameter
	private static int widenedByref(Func<object, int> next, object value) => next(value);

	private static MethodInfo method(string name) =>
		typeof(DetourChainTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
	private static DetourRegistration detour(string localId, string methodName) =>
		new(IlTest.OwnerId, localId, method(methodName));

	private static Installed build(MethodBase target, params DetourRegistration[] detours) {
		int slot = DetourDispatch.AllocateSlot();
		var chain = DetourChain.Build(target, slot, [.. detours]);
		DetourDispatch.SetChain(slot, chain.Entry, chain.State);
		return new Installed(chain, slot);
	}
	private static Installed build(string targetName, params DetourRegistration[] detours) =>
		build(method(targetName), detours);

	private readonly record struct Installed(DetourChain Chain, int Slot) {
		public object? Invoke(params object?[] args) {
			try {
				return Chain.Head.Invoke(null, args);
			} catch (TargetInvocationException ex) when (ex.InnerException is not null) {
				throw ex.InnerException;
			}
		}
	}

	// ==========================================================================================
	// composition
	[Fact]
	public static void SingleDetourWorks() {
		Installed chain = build(nameof(compute), detour("a", nameof(doubles)));

		// Compute(3) is 4, doubled is 8
		Assert.Equal(8, chain.Invoke(3));
	}

	[Fact]
	public static void ManyDetoursWork() {
		Installed chain = build(
			nameof(compute),
			detour("a", nameof(doubles)),
			detour("b", nameof(doubles)),
			detour("c", nameof(doubles))
		);

		// Compute(3) is 4, times 2^3 is 32
		Assert.Equal(32, chain.Invoke(3));
	}

	[Fact]
	public static void FirstInChainIsOutermostAssumingEachCallsNext() {
		Installed chain = build(
			nameof(compute),
			detour("outer", nameof(doubles)),
			detour("inner", nameof(addsTen))
		);

		// Compute(3) is 4, then +10 is 14, then doubled is 28; reverse order would yield 18
		Assert.Equal(28, chain.Invoke(3));
	}

	[Fact]
	public static void NeverCallingNextShortCircuitsChain() {
		Installed chain = build(
			nameof(compute),
			detour("outer", nameof(bypasses)),
			detour("inner", nameof(doubles))
		);

		Assert.Equal(99, chain.Invoke(3));
	}

	[Fact]
	public static void VoidReturningTargetIsSupported() {
		log.Clear();
		Installed chain = build(nameof(record), detour("a", nameof(prefixes)));

		chain.Invoke("hi");

		Assert.Equal(["[hi]"], log);
	}

	[Fact]
	public static void InstanceTargetPassesReceiverAsAnOrdinaryParameter() {
		Accumulator instance = new() { Factor = 5 };
		Installed chain = build(
			typeof(Accumulator).GetMethod(nameof(Accumulator.Scale))!,
			detour("a", nameof(offsets))
		);

		// Scale(4) is 20, then +1
		Assert.Equal(21, chain.Invoke(instance, 4));
	}

	[Fact]
	public static void AByrefParameterIsPassedThrough() {
		Installed chain = build(nameof(bump), detour("a", nameof(increments)));
		object?[] arguments = [7];

		// bump increments in place and returns the new value + the detour adds 100 to the result
		Assert.Equal(108, chain.Invoke(arguments));
		Assert.Equal(8, arguments[0]);
	}

	// ==========================================================================================
	// widening
	[Fact]
	public static void WidenedReferenceTypeParamPassesThrough() {
		Installed chain = build(nameof(unwrap), detour("a", nameof(widensPayload)));

		Assert.Equal(6, chain.Invoke(new Payload { Slot = 5 }));
	}

	[Fact]
	public static void WidenedValueTypeParamGoesThroughBoxing() {
		Installed chain = build(nameof(compute), detour("a", nameof(widensInt)));

		Assert.Equal(8, chain.Invoke(3));
	}

	[Fact]
	public static void WidenedInstanceParamPassesThrough() {
		Accumulator instance = new() { Factor = 5 };
		Installed chain = build(
			typeof(Accumulator).GetMethod(nameof(Accumulator.Scale))!,
			detour("a", nameof(widensWithInstance))
		);

		Assert.Equal(21, chain.Invoke(instance, 4));
	}

	[Fact]
	public static void WideningAndNonWideningInSameChainWork() {
		Installed chain = build(
			nameof(compute),
			detour("widened", nameof(widensInt)),
			detour("plain", nameof(addsTen))
		);

		// Compute(3) is 4, +10 is 14, doubled is 28: the boxing round trip must not disturb the result
		Assert.Equal(28, chain.Invoke(3));
	}

	[Fact]
	public static void WideningDetourPassingTheWrongTypeIsAttributedInException() {
		Installed chain = build(nameof(unwrap), detour("guilty", nameof(passesWrongType)));

		DetourArgumentException ex =
			Assert.Throws<DetourArgumentException>(() => chain.Invoke(new Payload { Slot = 5 }));

		Assert.Equal(IlTest.OwnerId, ex.OwnerId);
		Assert.Equal("guilty", ex.LocalId);
		Assert.Equal(0, ex.ParameterIndex);
	}

	[Fact]
	public static void WideningDetourPassingNullForAValueTypeIsRejected() {
		Installed chain = build(nameof(compute), detour("guilty", nameof(passesNull)));

		DetourArgumentException ex = Assert.Throws<DetourArgumentException>(() => chain.Invoke(3));

		Assert.Equal("guilty", ex.LocalId);
	}

	[Fact]
	public static void NullIsAcceptedWhereAReferenceTypeIsExpected() {
		Installed chain = build(nameof(unwrap), detour("a", nameof(widensPayload)));

		Assert.Throws<NullReferenceException>(() => chain.Invoke([null]));
	}

	// ==========================================================================================
	// validation
	[Fact]
	public static void EmptyChainIsRejected() =>
		Assert.Throws<InternalStateException>(() => build(nameof(compute)));

	[Fact]
	public static void ImplWithWrongParameterCountIsRejected() =>
		Assert.Throws<ArgumentException>(
			() => build(nameof(compute), detour("a", nameof(wrongParameterCount)))
		);

	[Fact]
	public static void ImplWithNonDelegateFirstParamIsRejected() =>
		Assert.Throws<ArgumentException>(
			() => build(nameof(compute), detour("a", nameof(firstParamNotDelegate)))
		);

	[Fact]
	public static void ImplWithWrongReturnTypeIsRejected() =>
		Assert.Throws<ArgumentException>(
			() => build(nameof(compute), detour("a", nameof(wrongReturn)))
		);

	[Fact]
	public static void ImplThatNarrowsAParamIsRejected() =>
		Assert.Throws<ArgumentException>(
			() => build(nameof(unwrap), detour("a", nameof(narrowedParameter)))
		);

	[Fact]
	public static void ImplThatWidensTheReturnTypeIsRejected() =>
		Assert.Throws<ArgumentException>(
			() => build(nameof(unwrap), detour("a", nameof(widenedReturn)))
		);

	[Fact]
	public static void ImplWithMismatchedNextIsRejected() =>
		Assert.Throws<ArgumentException>(
			() => build(nameof(compute), detour("a", nameof(mismatchedNext)))
		);

	[Fact]
	public static void ImplWideningAByrefParamIsRejected() =>
		Assert.Throws<ArgumentException>(
			() => build(nameof(bump), detour("a", nameof(widenedByref)))
		);

	[Fact]
	public static void ImplWithAnUnrelatedParamTypeIsRejected() =>
		Assert.Throws<ArgumentException>(
			() => build(nameof(record), detour("a", nameof(doubles)))
		);
}
