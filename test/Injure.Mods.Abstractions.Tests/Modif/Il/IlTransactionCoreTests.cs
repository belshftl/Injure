// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Tests.Modif.Il;

public sealed class IlTransactionCoreTests {
	private static IlTransactionCore open(IlMethodBody body, IlOwnerInfo info = default) => new(body, IlTest.OwnerId, IlTest.LocalId, info, null);
	private static int boundaryOf(IlMethodBody body, IlAnchorId anchor) => body.GetAnchorBoundary(anchor);

	// ==========================================================================================
	// insertion position
	[Fact]
	public static void InsertionEmitsBeforeBoundaryAnchor() {
		IlMethodBody body = new BodyBuilder().Nop().Ret().Build();
		IlAnchorId retAnchor = body.GetBoundaryAnchor(1);
		IlTransactionCore core = open(body);
		core.EmitAtBoundary(1, static e => { e.Nop(); e.Nop(); });
		core.Commit();

		Assert.Equal(4, body.Instructions.Count);
		Assert.Equal(3, boundaryOf(body, retAnchor));
		Assert.Equal(ILOpCode.Ret, body.Instructions[3].OpCode);
	}

	[Fact]
	public static void ExistingBranchKeepsTargetingItsOriginalInstruction() {
		IlMethodBody body = new BodyBuilder().Br(2).Nop().Ret().Build();
		IlTransactionCore core = open(body);
		core.EmitAtBoundary(2, static e => e.Nop());
		core.Commit();

		IlBranchOperand branch = Assert.IsType<IlBranchOperand>(body.Instructions[0].Operand);
		Assert.Equal(3, boundaryOf(body, branch.Target));
		Assert.Equal(ILOpCode.Ret, body.Instructions[3].OpCode);
	}

	[Fact]
	public static void EmittingAtTryStartEmitsOutsideProtectedRegion() {
		BodyBuilder builder = new();
		builder.Leave(3).Add(ILOpCode.Endfinally).Nop().Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, 0, 1, 1, 2);
		IlMethodBody body = builder.Build();

		IlTransactionCore core = open(body);
		core.EmitAtBoundary(0, static e => e.Nop());
		core.Commit();

		IlExceptionRegion region = body.ExceptionRegions[0];
		Assert.Equal(1, boundaryOf(body, region.TryStart));
		Assert.Equal(ILOpCode.Nop, body.Instructions[0].OpCode);
	}

	[Fact]
	public static void EmittingAtTryEndEmitsInsideProtectedRegion() {
		BodyBuilder builder = new();
		builder.Leave(3).Add(ILOpCode.Endfinally).Nop().Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, 0, 1, 1, 2);
		IlMethodBody body = builder.Build();

		IlTransactionCore core = open(body);
		core.EmitAtBoundary(1, static e => e.Nop());
		core.Commit();

		IlExceptionRegion region = body.ExceptionRegions[0];
		Assert.Equal(0, boundaryOf(body, region.TryStart));
		Assert.Equal(2, boundaryOf(body, region.TryEnd));
		Assert.Equal(ILOpCode.Nop, body.Instructions[1].OpCode);
	}

	[Fact]
	public static void FragmentsAtOneBoundaryApplyInEmitOrder() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		core.EmitAtBoundary(0, static e => e.LdcI4(1));
		core.EmitAtBoundary(0, static e => e.LdcI4(2));
		core.EmitAtBoundary(0, static e => e.LdcI4(3));
		core.Commit();

		int[] values = body.Instructions
			.Take(3)
			.Select(i => Assert.IsType<IlInt32Operand>(i.Operand).Value)
			.ToArray();
		Assert.Equal([1, 2, 3], values);
	}

	[Fact]
	public static void EmittedInstructionsCarryManipulatorProvenance() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		core.EmitAtBoundary(0, static e => e.Nop());
		core.Commit();

		Assert.Equal(IlTest.OwnerId, body.Instructions[0].Provenance.GetOwnerId());
		Assert.Equal(IlTest.LocalId, body.Instructions[0].Provenance.GetLocalId());
		Assert.True(body.Instructions[1].Provenance.IsUnknown);
	}

	// ==========================================================================================
	// commit atomicity
	[Fact]
	public static void NoEditsMeansNoPendingWork() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());

		Assert.False(core.HasPendingEdits);
		core.EmitAtBoundary(0, static e => e.Nop());
		Assert.True(core.HasPendingEdits);
	}

	[Fact]
	public static void AbortDiscardsEverything() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		core.EmitAtBoundary(0, static e => e.Nop());
		core.Abort();

		Assert.Single(body.Instructions);
	}

	[Fact]
	public static void CommitInvalidatesCachedStackHeight() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		body.ComputedMaxStack = 3;
		IlTransactionCore core = open(body);
		core.EmitAtBoundary(0, static e => e.Nop());
		core.Commit();

		Assert.Null(body.ComputedMaxStack);
	}

	// ==========================================================================================
	// labels
	[Fact]
	public static void ForwardBranchToLabelMarkedLaterResolves() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		IlLabel target = core.DefineLabel();
		core.EmitAtBoundary(0, e => {
			e.Br(target);
			e.Nop();
			e.MarkLabel(target);
			e.Nop();
		});
		core.Commit();

		IlBranchOperand branch = Assert.IsType<IlBranchOperand>(body.Instructions[0].Operand);
		Assert.Equal(2, boundaryOf(body, branch.Target));
	}

	[Fact]
	public static void LabelMarkedInLaterFragmentResolves() {
		IlMethodBody body = new BodyBuilder().Nop().Ret().Build();
		IlTransactionCore core = open(body);
		IlLabel target = core.DefineLabel();
		core.EmitAtBoundary(0, e => e.Br(target));
		core.EmitAtBoundary(1, e => e.MarkLabel(target));
		core.Commit();

		IlBranchOperand branch = Assert.IsType<IlBranchOperand>(body.Instructions[0].Operand);
		Assert.Equal(2, boundaryOf(body, branch.Target));
	}

	[Fact]
	public static void UnmarkedLabelFailsAtCommit() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		IlLabel target = core.DefineLabel();
		core.EmitAtBoundary(0, e => e.Br(target));

		Assert.ThrowsAny<Exception>(core.Commit);
		Assert.Single(body.Instructions);
	}

	[Fact]
	public static void LabelCantBeMarkedTwice() {
		IlMethodBody body = new BodyBuilder().Nop().Ret().Build();
		IlTransactionCore core = open(body);
		IlLabel target = core.DefineLabel();

		Assert.ThrowsAny<Exception>(() => {
			core.EmitAtBoundary(0, e => { e.MarkLabel(target); e.Nop(); });
			core.EmitAtBoundary(1, e => e.MarkLabel(target));
			core.Commit();
		});
	}

	[Fact]
	public static void LabelFromAnotherTransactionIsRejected() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlLabel foreign = open(new BodyBuilder().Ret().Build()).DefineLabel();
		IlTransactionCore core = open(body);

		Assert.ThrowsAny<Exception>(() => core.EmitAtBoundary(0, e => e.Br(foreign)));
	}

	// ==========================================================================================
	// matching
	[Fact]
	public static void PatternsMatchCanonicalInstructions() {
		IlMethodBody body = new BodyBuilder().LdcI4(0).Pop().LdcI4(0).Pop().Ret().Build();
		IlTransactionCore core = open(body);

		IlMatches matches = core.MatchAll([MatchIl.LdcI4(0), MatchIl.Pop], IlProvenanceConstr.Any);

		Assert.Equal(2, matches.Count);
	}

	[Fact]
	public static void MatchesRemainValidAcrossEmits() {
		IlMethodBody body = new BodyBuilder().LdcI4(0).Pop().LdcI4(0).Pop().Ret().Build();
		IlTransactionCore core = open(body);
		IlMatches matches = core.MatchAll([MatchIl.LdcI4(0), MatchIl.Pop], IlProvenanceConstr.Any);

		foreach (IlMatch match in matches)
			match.EmitBefore(static e => e.Nop());
		core.Commit();

		Assert.Equal(7, body.Instructions.Count);
		Assert.Equal(ILOpCode.Nop, body.Instructions[0].OpCode);
		Assert.Equal(ILOpCode.Nop, body.Instructions[3].OpCode);
	}

	[Fact]
	public static void MatchesDontOverlap() {
		IlMethodBody body = new BodyBuilder().Nop().Nop().Nop().Ret().Build();
		IlTransactionCore core = open(body);

		IlMatches matches = core.MatchAll([MatchIl.Nop, MatchIl.Nop], IlProvenanceConstr.Any);

		Assert.Equal(1, matches.Count);
	}

	[Fact]
	public static void RequireSingleFailsIfWrongCardinality() {
		IlMethodBody body = new BodyBuilder().Nop().Nop().Ret().Build();
		IlTransactionCore core = open(body);

		void a() {
			IlMatches none = core.MatchAll([MatchIl.Ldnull], IlProvenanceConstr.Any);
			_ = none.RequireSingle();
		}
		Assert.Throws<IlMatchException>(a);

		void b() {
			IlMatches several = core.MatchAll([MatchIl.Nop], IlProvenanceConstr.Any);
			_ = several.RequireSingle();
		}
		Assert.Throws<IlMatchException>(b);
	}

	[Fact]
	public static void ProvenanceConstraintsFilterMatches() {
		IlMethodBody body = new BodyBuilder().Nop().Ret().Build();
		IlTransactionCore first = open(body);
		first.EmitAtBoundary(0, static e => e.Nop());
		first.Commit();

		IlTransactionCore second = open(body);
		Assert.Equal(
			1,
			second.MatchAll([MatchIl.Nop], IlProvenanceConstr.AllFromOwner(IlTest.OwnerId)).Count
		);
		Assert.Equal(1, second.MatchAll([MatchIl.Nop], IlProvenanceConstr.AllUnknown).Count);
		Assert.Equal(2, second.MatchAll([MatchIl.Nop], IlProvenanceConstr.Any).Count);
	}

	[Fact]
	public static void UniformProvenanceRequiresEveryMatchedInstructionToAgree() {
		IlMethodBody body = new BodyBuilder().Nop().Nop().Ret().Build();
		IlTransactionCore core = open(body);

		IlMatch match = core.MatchAll([MatchIl.Nop, MatchIl.Nop], IlProvenanceConstr.Any).RequireSingle();

		Assert.True(match.TryGetUniformProvenance(out IlProvenance provenance));
		Assert.Null(provenance.OwnerId);
	}

	[Fact]
	public static void AParameterlessFactoryMatchesAnyOperand() {
		IlMethodBody body = new BodyBuilder().Ldloc(0).Ldloc(3).Ldloc(300).Pop().Ret().Build(locals: [.. Enumerable.Repeat<IlTypeRef>(IlTest.Int32, 301)]);
		IlTransactionCore core = open(body);

		Assert.Equal(3, core.MatchAll([MatchIl.Ldloc()], IlProvenanceConstr.Any).Count);
	}

	[Fact]
	public static void AParameterlessFactoryStillChecksTheOpCode() {
		IlMethodBody body = new BodyBuilder().Ldarg(0).LdcI4(1).Pop().Pop().Ret().Build(IlTest.Sig(IlTest.Void, IlTest.Int32));
		IlTransactionCore core = open(body);

		Assert.Equal(1, core.MatchAll([MatchIl.LdcI4()], IlProvenanceConstr.Any).Count);
	}

	[Fact]
	public static void AnyOperandMixesWithExactElements() {
		IlMethodRef first = IlTest.Method("First", IlTest.Void);
		IlMethodRef second = IlTest.Method("Second", IlTest.Void);
		IlMethodBody body = new BodyBuilder().LdcI4(1).Call(first).LdcI4(2).Call(second).LdcI4(1).Call(second).Ret().Build();
		IlTransactionCore core = open(body);

		Assert.Equal(3, core.MatchAll([MatchIl.LdcI4(), MatchIl.Call()], IlProvenanceConstr.Any).Count);
		Assert.Equal(2, core.MatchAll([MatchIl.LdcI4(1), MatchIl.Call()], IlProvenanceConstr.Any).Count);
		Assert.Equal(2, core.MatchAll([MatchIl.LdcI4(), MatchIl.Call(second)], IlProvenanceConstr.Any).Count);
	}

	[Fact]
	public static void BranchesMatchWithAnyTarget() {
		IlMethodBody body = new BodyBuilder().Br(2).Brtrue(3).Leave(4).Switch(0, 4).Ret().Build();
		IlTransactionCore core = open(body);

		Assert.Equal(1, core.MatchAll([MatchIl.Br()], IlProvenanceConstr.Any).Count);
		Assert.Equal(1, core.MatchAll([MatchIl.Brtrue()], IlProvenanceConstr.Any).Count);
		Assert.Equal(0, core.MatchAll([MatchIl.Brfalse()], IlProvenanceConstr.Any).Count);
		Assert.Equal(1, core.MatchAll([MatchIl.Leave()], IlProvenanceConstr.Any).Count);
		Assert.Equal(1, core.MatchAll([MatchIl.Switch()], IlProvenanceConstr.Any).Count);
	}

	// ==========================================================================================
	// locals
	private static ImmutableArray<IlTypeRef> int32Locals(int count) => Enumerable.Repeat<IlTypeRef>(IlTest.Int32, count).ToImmutableArray();
	private static int localIndexOf(IlInstruction instr) => Assert.IsType<IlLocalOperand>(instr.Operand).Index;

	private const string reloadableAssembly = "Reloadable";

	private static IlOwnerInfo reloadableInfo() => new(
		new Dictionary<string, (string, bool)> { [reloadableAssembly] = ("reloadable", true) },
		new HashSet<string>()
	);

	private static IlNamedTypeRef reloadableStruct() => new(
		new IlTypeScope.Assembly(new IlAssemblyIdentity(reloadableAssembly, null, null, [], default)),
		null,
		"Mod",
		"State",
		0,
		IlNamedTypeKind.ValueType
	);

	[Fact]
	public static void DeclaredLocalIsAppendedAfterExistingLocals() {
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: [IlTest.Int32]);
		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Object);
		core.EmitAtBoundary(0, e => { e.Ldloc(local); e.Pop(); });
		core.Commit();

		Assert.Equal(new IlTypeRef[] { IlTest.Int32, IlTest.Object }, body.Locals.ToArray());
		Assert.Equal(1, localIndexOf(body.Instructions[0]));
	}

	[Fact]
	public static void DeclaredLocalsGetConsecutiveIndices() {
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: int32Locals(2));
		IlTransactionCore core = open(body);
		IlLocal first = core.DeclareLocal(IlTest.Int32);
		IlLocal second = core.DeclareLocal(IlTest.Object);
		core.EmitAtBoundary(0, e => { e.Ldloc(first); e.Pop(); e.Ldloc(second); e.Pop(); });
		core.Commit();

		Assert.Equal(4, body.Locals.Length);
		Assert.Equal(2, localIndexOf(body.Instructions[0]));
		Assert.Equal(3, localIndexOf(body.Instructions[2]));
	}

	[Fact]
	public static void LaterTransactionsSeeEarlierDeclaredLocals() {
		IlMethodBody body = new BodyBuilder().Ret().Build();

		IlTransactionCore first = open(body);
		IlLocal a = first.DeclareLocal(IlTest.Int32);
		first.EmitAtBoundary(0, e => { e.Ldloc(a); e.Pop(); });
		first.Commit();

		IlTransactionCore second = open(body);
		IlLocal b = second.DeclareLocal(IlTest.Object);
		second.EmitAtBoundary(0, e => { e.Ldloc(b); e.Pop(); });
		second.Commit();

		Assert.Equal(new IlTypeRef[] { IlTest.Int32, IlTest.Object }, body.Locals.ToArray());
		Assert.Equal(1, localIndexOf(body.Instructions[0]));
		Assert.Equal(0, localIndexOf(body.Instructions[2]));
	}

	[Fact]
	public static void ADeclaredLocalMayBeReferencedByRawIndex() {
		// commit-time index validation must count locals declared by the same transaction
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Int32);
		core.EmitAtBoundary(0, e => { e.Ldloc(local.Index); e.Pop(); });
		core.Commit();

		Assert.Single(body.Locals);
	}

	[Fact]
	public static void RawIndexPastDeclaredLocalsIsRejectedAtCommit() {
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: [IlTest.Int32]);
		IlTransactionCore core = open(body);
		_ = core.DeclareLocal(IlTest.Int32);
		core.EmitAtBoundary(0, static e => { e.Ldloc(2); e.Pop(); });

		Assert.Throws<IlPipelineException>(core.Commit);
		Assert.Single(body.Locals);
		Assert.Single(body.Instructions);
	}

	[Fact]
	public static void FirstLocalOfALocallessMethodSetsInitlocals() {
		IlMethodBody body = new BodyBuilder().Ret().Build(initLocals: false);
		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Int32);
		core.EmitAtBoundary(0, e => { e.Ldloc(local); e.Pop(); });
		core.Commit();

		Assert.True(body.InitLocals);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public static void AnExistingInitlocalsFlagIsKept(bool initLocals) {
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: [IlTest.Int32], initLocals: initLocals);
		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Int32);
		core.EmitAtBoundary(0, e => { e.Ldloc(local); e.Pop(); });
		core.Commit();

		Assert.Equal(initLocals, body.InitLocals);
	}

	[Fact]
	public static void DeclaringALocalDropsTheLocalSignatureOrigin() {
		IlLocalSignatureOrigin origin = new(new IlModuleIdentity(Guid.Empty, "Test"), 7);
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: [IlTest.Int32], localsOrigin: origin);
		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Int32);
		core.EmitAtBoundary(0, e => { e.Ldloc(local); e.Pop(); });
		core.Commit();

		Assert.False(body.LocalSignatureOrigin.IsValid);
	}

	[Fact]
	public static void EditsWithoutDeclaredLocalsKeepTheLocalSignatureOrigin() {
		IlLocalSignatureOrigin origin = new(new IlModuleIdentity(Guid.Empty, "Test"), 7);
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: [IlTest.Int32], localsOrigin: origin);
		IlTransactionCore core = open(body);
		core.EmitAtBoundary(0, static e => e.Nop());
		core.Commit();

		Assert.Equal(origin, body.LocalSignatureOrigin);
	}

	[Fact]
	public static void DeclaringALocalIsNotAPendingEdit() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());
		_ = core.DeclareLocal(IlTest.Int32);

		Assert.False(core.HasPendingEdits);
	}

	[Fact]
	public static void AbortDiscardsDeclaredLocals() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Int32);
		core.EmitAtBoundary(0, e => { e.Ldloc(local); e.Pop(); });
		core.Abort();

		Assert.Empty(body.Locals);
		Assert.False(body.InitLocals);
	}

	[Fact]
	public static void FailedCommitDiscardsDeclaredLocals() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		IlLabel target = core.DefineLabel();
		IlLocal local = core.DeclareLocal(IlTest.Int32);
		core.EmitAtBoundary(0, e => { e.Ldloc(local); e.Brtrue(target); });

		Assert.ThrowsAny<Exception>(core.Commit);
		Assert.Empty(body.Locals);
		Assert.False(body.InitLocals);
	}

	[Fact]
	public static void LocalFromAnotherTransactionIsRejected() {
		IlLocal foreign = open(new BodyBuilder().Ret().Build()).DeclareLocal(IlTest.Int32);
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());

		Assert.Throws<IlPipelineException>(() => core.EmitAtBoundary(0, e => e.Ldloc(foreign)));
	}

	[Fact]
	public static void VoidLocalIsRejected() =>
		Assert.Throws<ArgumentException>(() => open(new BodyBuilder().Ret().Build()).DeclareLocal(IlTest.Void));

	[Fact]
	public static void ReloadableValueTypeLocalIsRejected() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build(), reloadableInfo());

		Assert.Throws<IlCollectibleReferenceException>(() => core.DeclareLocal(reloadableStruct()));
	}

	[Fact]
	public static void ByrefToReloadableValueTypeLocalIsAccepted() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build(), reloadableInfo());

		IlLocal local = core.DeclareLocal(IlRefFactory.ByRef(reloadableStruct()));

		Assert.Equal(0, local.Index);
	}

	[Fact]
	public static void DeclaredLocalsGetTheTransactionsProvenance() {
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: [IlTest.Int32]);
		IlTransactionCore core = open(body);
		IlLocal local = core.DeclareLocal(IlTest.Object);
		core.EmitAtBoundary(0, e => { e.Ldloc(local); e.Pop(); });
		core.Commit();

		Assert.Equal(
			new[] { default, new InternalIlProvenance(IlTest.OwnerId, IlTest.LocalId) },
			body.LocalsProvenance.ToArray()
		);
	}

	[Fact]
	public static void LocalsProvenanceIsProperlyStampedOn() {
		IlMethodBody body = new BodyBuilder().Ret().Build();

		IlTransactionCore first = new(body, "first", "a", default, null);
		IlLocal a = first.DeclareLocal(IlTest.Int32);
		first.EmitAtBoundary(0, e => { e.Ldloc(a); e.Pop(); });
		first.Commit();

		IlTransactionCore second = new(body, "second", "b", default, null);
		IlLocal b = second.DeclareLocal(IlTest.Int32);
		IlLocal c = second.DeclareLocal(IlTest.Object);
		second.EmitAtBoundary(0, e => { e.Ldloc(b); e.Pop(); e.Ldloc(c); e.Pop(); });
		second.Commit();

		Assert.Equal(
			new[] { new InternalIlProvenance("first", "a"), new InternalIlProvenance("second", "b"), new InternalIlProvenance("second", "b") },
			body.LocalsProvenance.ToArray()
		);
	}

	[Fact]
	public static void CloneKeepsLocalsProvenance() {
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: [IlTest.Int32], baselineProvenance: new InternalIlProvenance("TestGame", null));

		Assert.Equal(body.LocalsProvenance, body.Clone().LocalsProvenance);
	}

	[Theory]
	[InlineData(ushort.MaxValue, true)] // a local index can't go above 0xffff
	[InlineData(ushort.MaxValue + 1, false)]
	public static void DeclaredIndexMustFitInAU16(int existing, bool accepted) {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build(locals: int32Locals(existing)));

		if (accepted)
			Assert.Equal(existing, core.DeclareLocal(IlTest.Int32).Index);
		else
			Assert.Throws<IlPipelineException>(() => core.DeclareLocal(IlTest.Int32));
	}

	// ==========================================================================================
	// validation
	[Theory]
	[InlineData(ILOpCode.Ldc_i4_s)]
	[InlineData(ILOpCode.Ldc_i4_0)]
	[InlineData(ILOpCode.Ldloc_2)]
	[InlineData(ILOpCode.Br_s)]
	public static void MatchingRejectsCompactEncodings(ILOpCode opCode) {
		ArgumentException ex = Assert.Throws<ArgumentException>(() => MatchIl.Raw(opCode));
		Assert.Contains("is a compact encoding", ex.Message, StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => MatchIl.Raw(opCode, new IlInt32Operand(0)));
	}

	[Theory]
	[InlineData(ILOpCode.Ldc_i4_s, "ldc.i4.s is a compact encoding; use ldc.i4")]
	[InlineData(ILOpCode.Ldarg_0, "ldarg.0 is a compact encoding; use ldarg")]
	public static void RawEmissionRejectsCompactEncodings(ILOpCode opCode, string expected) {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());

		ArgumentException ex = Assert.Throws<ArgumentException>(() => core.EmitAtBoundary(0, e => e.RawNonbranch(opCode, new IlInt32Operand(0))));
		Assert.StartsWith(expected, ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public static void RawEmissionAcceptsCanonicalBranches() {
		IlMethodBody body = new BodyBuilder().Ret().Build();
		IlTransactionCore core = open(body);
		IlLabel target = core.DefineLabel();
		core.EmitAtBoundary(0, e => { e.RawBranch(ILOpCode.Br, target); e.MarkLabel(target); });
		core.Commit();

		Assert.Equal(ILOpCode.Br, body.Instructions[0].OpCode);
	}

	[Fact]
	public static void RawEmissionRejectsCompactBranches() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());
		IlLabel target = core.DefineLabel();

		Assert.Throws<ArgumentException>(() => core.EmitAtBoundary(0, e => e.RawBranch(ILOpCode.Br_s, target)));
	}

	[Theory]
	[InlineData(ILOpCode.Ldc_i4_0)]
	[InlineData(ILOpCode.Br_s)]
	[InlineData(ILOpCode.Volatile)]
	[InlineData((ILOpCode)0x24)]
	public static void InstructionsRejectOpCodesThatArentCanonicalEncodings(ILOpCode opCode) =>
		Assert.Throws<InternalStateException>(() => new IlInstruction(
			new IlInstructionId(1),
			opCode,
			IlNoneOperand.Instance,
			null,
			IlInstruction.NoOriginalOffset,
			default
		));

	[Fact]
	public static void InstructionsRejectANullOperand() =>
		Assert.Throws<InternalStateException>(static () => new IlInstruction(
			new IlInstructionId(1),
			ILOpCode.Nop,
			null!,
			null,
			IlInstruction.NoOriginalOffset,
			default
		));

	[Fact]
	public static void BodiesRejectADefaultInstruction() {
		// it bypassed the constructor, but its ID is invalid too, which the body checks
		InternalStateException ex = Assert.Throws<InternalStateException>(static () => IlMethodBody.CreateDecoded(
			IlTest.Method("Target", IlTest.Void),
			true,
			[],
			[],
			default,
			[default],
			[BodyBuilder.Anchor(0), BodyBuilder.Anchor(1)],
			[]
		));
		Assert.Contains("IDs must be valid", ex.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(ILOpCode.Ldarg, -1)]
	[InlineData(ILOpCode.Ldarg, 65536)]
	[InlineData(ILOpCode.Starg, 65536)]
	[InlineData(ILOpCode.Ldloc, -1)]
	[InlineData(ILOpCode.Ldloca, 65536)]
	[InlineData(ILOpCode.Stloc, 70000)]
	public static void RawEmissionRejectsOutOfRangeIndices(ILOpCode opCode, int index) {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());
		IlOperand operand = opCode is ILOpCode.Ldarg or ILOpCode.Starg ? new IlArgumentOperand(index) : new IlLocalOperand(index);

		ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => core.EmitAtBoundary(0, e => e.RawNonbranch(opCode, operand)));
		Assert.Equal("operand", ex.ParamName);
	}

	[Fact]
	public static void RawEmissionAcceptsTheLargestLocalIndex() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());

		// the commit-time check against the method's local count is separate, this only checks the range
		core.EmitAtBoundary(0, static e => { e.RawNonbranch(ILOpCode.Ldloc, new IlLocalOperand(ushort.MaxValue)); e.Pop(); });
	}

	[Theory]
	[InlineData(ILOpCode.Volatile)]
	[InlineData(ILOpCode.Constrained)]
	[InlineData((ILOpCode)0x24)]
	public static void PrefixesAndUndefinedOpCodesAreRejected(ILOpCode opCode) {
		Assert.Throws<ArgumentException>(() => MatchIl.Raw(opCode));
		Assert.Throws<ArgumentException>(() => MatchIl.Raw(opCode, IlNoneOperand.Instance));
	}

	[Fact]
	public static void AFailedMatchSaysAnyOperand() {
		IlTransactionCore core = open(new BodyBuilder().Ret().Build());

		IlMatchException ex = Assert.Throws<IlMatchException>(() => core.MatchNext(0, [MatchIl.Ldloc(), MatchIl.Ret], IlProvenanceConstr.Any));

		Assert.Contains("0. ldloc <any operand>\n", ex.Message.ReplaceLineEndings("\n"), StringComparison.Ordinal);
		Assert.Contains("1. ret\n", ex.Message.ReplaceLineEndings("\n"), StringComparison.Ordinal);
	}
}
