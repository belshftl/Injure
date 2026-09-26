// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Detours;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Tests.Modif.Detours;

public sealed class DetourTransformTests {
	private sealed class Receiver {
#pragma warning disable CA1822 // method can be marked as static
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Scale(int value) => value;
#pragma warning restore CA1822 // method can be marked as static
	}

	private struct Value {
		public int Field;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Incr() => ++Field;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int target(int value) => value;

	private static readonly ILOpCode[] prologue = [
		ILOpCode.Ldc_i4,
		ILOpCode.Call,
		ILOpCode.Stloc,
		ILOpCode.Ldloc,
		ILOpCode.Brfalse,
		ILOpCode.Ldarg, // assumes only 1 arg on the method
		ILOpCode.Ldloc,
		ILOpCode.Calli,
		ILOpCode.Ret,
	];

	private static MethodInfo method(Type type, string name) =>
		type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

	/// <summary>
	/// Builds a body for <paramref name="target"/> that returns its first argument (or receiver),
	/// then applies the prologue to it.
	/// </summary>
	private static IlMethodBody apply(MethodInfo target, ImmutableArray<IlTypeRef> locals = default, bool initLocals = true) {
		var body = IlMethodBody.CreateEmpty(IlRefFactory.Method(target), locals, initLocals);
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, null);
		core.EmitAtBoundary(0, static e => { e.Ldarg(0); e.Ret(); });
		core.Commit();

		DetourTransform transform = new(static _ => throw new InternalStateException("the prologue must not need reflection"));
		return transform.Apply(new MethodIdentity(new ModuleId(1), target.MetadataToken), body, []);
	}

	private static int localIndexOf(IlInstruction instr) => Assert.IsType<IlLocalOperand>(instr.Operand).Index;

	[Fact]
	public static void PrologueReadsTheEntryOnceAndCallsThroughIt() {
		IlMethodBody body = apply(method(typeof(DetourTransformTests), nameof(target)));

		Assert.Equal(
			[.. prologue, ILOpCode.Ldarg, ILOpCode.Ret],
			body.Instructions.Select(static i => i.OpCode).ToArray()
		);

		IlMethodRef enter = Assert.IsType<IlMethodOperand>(body.Instructions[1].Operand).Method;
		Assert.Equal(nameof(DetourDispatch.Enter), enter.Name);

		// the same local is stored, tested, and called through, so a clear can't be between reads
		int entry = localIndexOf(body.Instructions[2]);
		Assert.Equal(entry, localIndexOf(body.Instructions[3]));
		Assert.Equal(entry, localIndexOf(body.Instructions[6]));
	}

	[Fact]
	public static void FallthroughLandsOnTheOriginalBody() {
		IlMethodBody body = apply(method(typeof(DetourTransformTests), nameof(target)));

		IlBranchOperand branch = Assert.IsType<IlBranchOperand>(body.Instructions[4].Operand);
		Assert.Equal(9, body.GetAnchorBoundary(branch.Target));
	}

	[Fact]
	public static void EntryLocalIsANintAppendedToALocallessMethod() {
		IlMethodBody body = apply(method(typeof(DetourTransformTests), nameof(target)), initLocals: false);

		Assert.Equal(new IlTypeRef[] { IlRefFactory.Type(typeof(nint)) }, body.Locals.ToArray());
		Assert.Equal(0, localIndexOf(body.Instructions[2]));
		Assert.True(body.InitLocals);
	}

	[Fact]
	public static void EntryLocalGoesAfterExistingLocals() {
		IlMethodBody body = apply(
			method(typeof(DetourTransformTests), nameof(target)),
			[IlRefFactory.Type(typeof(int)), IlRefFactory.Type(typeof(string))],
			initLocals: false
		);

		Assert.Equal(3, body.Locals.Length);
		Assert.Equal(2, localIndexOf(body.Instructions[2]));
		Assert.False(body.InitLocals);
	}

	[Fact]
	public static void InstanceTargetPassesItsReceiverAsTheFirstChainArgument() {
		IlMethodBody body = apply(method(typeof(Receiver), nameof(Receiver.Scale)));

		// ldarg.0 (receiver), ldarg.1, then the entry and the calli
		Assert.Equal(0, Assert.IsType<IlArgumentOperand>(body.Instructions[5].Operand).Index);
		Assert.Equal(1, Assert.IsType<IlArgumentOperand>(body.Instructions[6].Operand).Index);
		Assert.Equal(ILOpCode.Ldloc, body.Instructions[7].OpCode);

		IlMethodSignature signature = Assert.IsType<IlCallSiteOperand>(body.Instructions[8].Operand).Signature;
		Assert.False(signature.HasThis);
		Assert.Equal(2, signature.ParameterTypes.Length);
	}

	[Fact]
	public static void PrologueIsStackBalanced() {
		IlMethodBody body = apply(method(typeof(DetourTransformTests), nameof(target)));

		// entry, then the argument; calli pops both and pushes the return value
		Assert.Equal(2, IlMaxStackAnalyzer.Analyze(body));
	}

	public static TheoryData<MethodBase> ChainSignatureTargets => new() {
		method(typeof(DetourTransformTests), nameof(target)),
		method(typeof(Receiver), nameof(Receiver.Scale)),
		method(typeof(Value), nameof(Value.Incr)),
		typeof(int).GetMethod(nameof(int.ToString), Type.EmptyTypes)!,
		typeof(Guid).GetMethod(nameof(Guid.ToByteArray), Type.EmptyTypes)!,
	};

#pragma warning disable xUnit1045 // TheoryData<T> type might not be serializable
	[Theory]
	[MemberData(nameof(ChainSignatureTargets))]
	public static void PrologueCalliSignatureMatchesTheChainHead(MethodBase target) {
		IlMethodSignature prologue = DetourTransform.ChainSignature(IlRefFactory.Method(target));
		IlTypeRef[] head = DetourChain.ParameterTypesOf(target).Select(IlRefFactory.Type).ToArray();

		Assert.Equal(head, prologue.ParameterTypes.ToArray());
	}
#pragma warning restore xUnit1045 // TheoryData<T> type might not be serializable

	[Fact]
	public static void AStructReceiverIsPassedAsByref() {
		IlMethodBody body = apply(method(typeof(Value), nameof(Value.Incr)));

		IlMethodSignature signature = Assert.IsType<IlCallSiteOperand>(body.Instructions[7].Operand).Signature;
		IlByRefTypeRef receiver = Assert.IsType<IlByRefTypeRef>(signature.ParameterTypes[0]);
		Assert.Equal(IlRefFactory.Type(typeof(Value)), receiver.ElementType);
	}
}
