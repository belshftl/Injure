// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Abstractions.Tests.MethodModification.Il;

public sealed class IlMaxStackAnalyzerTests {
	// ==========================================================================================
	// regression tests
	[Fact]
	public static void ModifiedVoidReturnStillCountsAsVoid() {
		IlTypeRef modifiedVoid = new IlModifiedTypeRef(IlTest.Named("System.Runtime.CompilerServices", "IsExternalInit", IlNamedTypeKind.Class), IlTest.Void, isRequired: true);
		IlMethodBody body = new BodyBuilder().Ret().Build(IlTest.Sig(modifiedVoid));
		Assert.Equal(0, IlMaxStackAnalyzer.Analyze(body));
	}

	// ==========================================================================================
	// heights
	[Fact]
	public static void EmptyReturnHasNoStack() =>
		Assert.Equal(0, IlMaxStackAnalyzer.Analyze(new BodyBuilder().Ret().Build()));

	[Fact]
	public static void ReturnValueCountsTowardsMaximum() {
		IlMethodBody body = new BodyBuilder().LdcI4(1).Ret().Build(IlTest.Sig(IlTest.Int32));
		Assert.Equal(1, IlMaxStackAnalyzer.Analyze(body));
	}

	[Fact]
	public static void DupPushesWithoutPoppingNet() {
		// ldc.i4; dup; pop; pop; ret -> peak 2
		IlMethodBody body = new BodyBuilder().LdcI4(1).Add(ILOpCode.Dup).Pop().Pop().Ret().Build();

		Assert.Equal(2, IlMaxStackAnalyzer.Analyze(body));
	}

	[Fact]
	public static void BranchTargetsMergeAtTheSameHeight() {
		// ldc.i4; brtrue -> 4; ldc.i4; br -> 5; ldc.i4; pop; ret
		BodyBuilder builder = new();
		builder.LdcI4(1).Brtrue(4).LdcI4(2).Br(5).LdcI4(3).Pop().Ret();

		Assert.Equal(1, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void ResultIsCachedOnTheBody() {
		IlMethodBody body = new BodyBuilder().LdcI4(1).Pop().Ret().Build();

		Assert.Null(body.ComputedMaxStack);
		Assert.Equal(1, IlMaxStackAnalyzer.Analyze(body));
		Assert.Equal(1, body.ComputedMaxStack);
		Assert.Equal(1, IlMaxStackAnalyzer.Analyze(body));
	}

	// ==========================================================================================
	// rejection
	[Fact]
	public static void UnderflowIsRejected() =>
		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(new BodyBuilder().Pop().Ret().Build()));

	[Fact]
	public static void ConflictingMergeHeightsAreRejected() {
		// ldc.i4; brtrue -> 3; ldc.i4; ret
		// (target reached at height 0 and 1)
		BodyBuilder builder = new();
		builder.LdcI4(1).Brtrue(3).LdcI4(2).Ret();

		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void FallingOffTheEndIsRejected() =>
		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(new BodyBuilder().Nop().Build()));

	[Fact]
	public static void ReturnAtNonZeroHeightIsRejected() {
		IlMethodBody body = new BodyBuilder().LdcI4(1).Ret().Build();

		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(body));
	}

	[Fact]
	public static void JmpRequiresEmptyStack() {
		IlMethodBody body = new BodyBuilder()
			.LdcI4(1)
			.Add(ILOpCode.Jmp, new IlMethodOperand(IlTest.Method("Other", IlTest.Void)))
			.Build();

		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(body));
	}

	[Fact]
	public static void TailCallMustBeFollowedByReturn() {
		IlInstructionPrefixes tail = new(IlPrefixFlags.Tail, null, 0, IlSkipChecks.None);
		BodyBuilder builder = new();
		builder.Add(ILOpCode.Call, new IlMethodOperand(IlTest.Method("Other", IlTest.Void)), tail).Nop().Ret();

		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void TailCallFollowedByReturnIsAccepted() {
		IlInstructionPrefixes tail = new(IlPrefixFlags.Tail, null, 0, IlSkipChecks.None);
		BodyBuilder builder = new();
		builder.Add(ILOpCode.Call, new IlMethodOperand(IlTest.Method("Other", IlTest.Void)), tail).Ret();

		Assert.Equal(0, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	// ==========================================================================================
	// unreachable code
	[Fact]
	public static void UnreachableCodeAfterReturnIsAccepted() {
		// ret; ldc.i4; ldc.i4  -> unreachable, falls off the end, contributes 2 to the maximum
		IlMethodBody body = new BodyBuilder().Ret().LdcI4(1).LdcI4(2).Build();

		Assert.Equal(2, IlMaxStackAnalyzer.Analyze(body));
	}

	[Fact]
	public static void UnreachableUnderflowIsAccepted() {
		IlMethodBody body = new BodyBuilder().Ret().Pop().Build();

		Assert.Equal(0, IlMaxStackAnalyzer.Analyze(body));
	}

	// ==========================================================================================
	// exception handling
	[Fact]
	public static void CatchHandlerStartsWithExceptionOnTheStack() {
		// try { leave -> 3 } catch { pop; leave -> 3 } ret
		BodyBuilder builder = new();
		builder.Leave(3).Pop().Leave(3).Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Catch, tryStart: 0, tryEnd: 1, handlerStart: 1, handlerEnd: 3);

		Assert.Equal(1, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void FinallyHandlerStartsEmpty() {
		// try { leave -> 3 } finally { nop; endfinally } ret
		BodyBuilder builder = new();
		builder.Leave(3).Nop().Add(ILOpCode.Endfinally).Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, tryStart: 0, tryEnd: 1, handlerStart: 1, handlerEnd: 3);

		Assert.Equal(0, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void FilterStartsWithExceptionOnTheStack() {
		// try { leave -> 5 } filter { pop; ldc.i4; endfilter } handler { pop; leave -> 5 } ret
		BodyBuilder builder = new();
		builder.Pop().Leave(7).LdcI4(1).Add(ILOpCode.Endfilter).Pop().Leave(7).Nop().Ret();
		builder.ExceptionRegion(
			IlExceptionRegionKind.Filter,
			tryStart: 0,
			tryEnd: 2,
			handlerStart: 4,
			handlerEnd: 6,
			filterStart: 2
		);

		// the try body pops nothing; height 1 comes from the filter and handler entries
		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void LeaveEmptiesTheStackAtItsTarget() {
		// try { ldc.i4; leave -> 3 } finally { endfinally } ret
		BodyBuilder builder = new();
		builder.LdcI4(1).Leave(3).Add(ILOpCode.Endfinally).Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, tryStart: 0, tryEnd: 2, handlerStart: 2, handlerEnd: 3);

		Assert.Equal(1, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void EndfinallyOutsideAHandlerIsRejected() {
		IlMethodBody body = new BodyBuilder().Add(ILOpCode.Endfinally).Build();

		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(body));
	}

	[Fact]
	public static void EndfilterOutsideAFilterIsRejected() {
		IlMethodBody body = new BodyBuilder().LdcI4(1).Add(ILOpCode.Endfilter).Build();

		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(body));
	}

	[Fact]
	public static void EnteringProtectedRegionWithNonEmptyStackIsRejected() {
		// ldc.i4; try { leave -> 3 } finally { endfinally } ret
		BodyBuilder builder = new();
		builder.LdcI4(1).Leave(4).Add(ILOpCode.Endfinally).Nop().Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, tryStart: 1, tryEnd: 2, handlerStart: 2, handlerEnd: 3);

		Assert.Throws<IlInvalidMethodException>(() => IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	// ==========================================================================================
	// signature-dependent effects
	[Fact]
	public static void CallPopsArgumentsAndReceiver() {
		IlMethodRef method = IlTest.Method("Instance", IlTest.InstanceSig(IlTest.Void, IlTest.Int32, IlTest.Int32));
		BodyBuilder builder = new();
		builder.LdcI4(0).LdcI4(1).LdcI4(2).Call(method).Ret();

		Assert.Equal(3, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void ExplicitThisDoesNotAddAReceiver() {
		IlMethodRef method = IlTest.Method("Explicit", IlTest.ExplicitThisSig(IlTest.Void, IlTest.Object, IlTest.Int32));
		BodyBuilder builder = new();
		builder.LdcI4(0).LdcI4(1).Call(method).Ret();

		Assert.Equal(2, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void VarargCallPopsOptionalParametersToo() {
		IlMethodRef method = IlTest.Method(
			"Sum",
			IlTest.VarargSig(IlTest.Int32, required: 1, IlTest.Int32, IlTest.Int32, IlTest.Int32)
		);
		BodyBuilder builder = new();
		builder.LdcI4(0).LdcI4(1).LdcI4(2).Call(method).Pop().Ret();

		Assert.Equal(3, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void NewobjDoesNotPopAReceiver() {
		IlMethodRef ctor = IlTest.Method(".ctor", IlTest.InstanceSig(IlTest.Void, IlTest.Int32));
		BodyBuilder builder = new();
		builder.LdcI4(0).Newobj(ctor).Pop().Ret();

		Assert.Equal(1, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void CalliPopsTheFunctionPointer() {
		BodyBuilder builder = new();
		builder.LdcI4(0).LdcI4(1).Calli(IlTest.Sig(IlTest.Void, IlTest.Int32)).Ret();

		Assert.Equal(2, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}

	[Fact]
	public static void SwitchReachesEveryTargetAndFallsThrough() {
		// ldc.i4; switch -> 3, 4; nop; nop; ret
		BodyBuilder builder = new();
		builder.LdcI4(0).Switch(3, 4).Nop().Nop().Ret();

		Assert.Equal(1, IlMaxStackAnalyzer.Analyze(builder.Build()));
	}
}
