// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Abstractions.Tests.MethodModification.Il;

public sealed class IlTransactionTests {
	private static IlTransactionCore open(IlMethodBody body) => new(body, IlTest.OwnerId, IlTest.LocalId);
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

		IlMatches matches = core.MatchAll([MatchIl.LdcI4(0), MatchIl.Pop], IlPatternProvenanceConstraint.Any);

		Assert.Equal(2, matches.Count);
	}

	[Fact]
	public static void MatchesRemainValidAcrossEmits() {
		IlMethodBody body = new BodyBuilder().LdcI4(0).Pop().LdcI4(0).Pop().Ret().Build();
		IlTransactionCore core = open(body);
		IlMatches matches = core.MatchAll([MatchIl.LdcI4(0), MatchIl.Pop], IlPatternProvenanceConstraint.Any);

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

		IlMatches matches = core.MatchAll([MatchIl.Nop, MatchIl.Nop], IlPatternProvenanceConstraint.Any);

		Assert.Equal(1, matches.Count);
	}

	[Fact]
	public static void RequireSingleFailsIfWrongCardinality() {
		IlMethodBody body = new BodyBuilder().Nop().Nop().Ret().Build();
		IlTransactionCore core = open(body);

		void a() {
			IlMatches none = core.MatchAll([MatchIl.Ldnull], IlPatternProvenanceConstraint.Any);
			_ = none.RequireSingle();
		}
		Assert.Throws<IlMatchException>(a);

		void b() {
			IlMatches several = core.MatchAll([MatchIl.Nop], IlPatternProvenanceConstraint.Any);
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
			second.MatchAll([MatchIl.Nop], IlPatternProvenanceConstraint.AllFromOwner(IlTest.OwnerId)).Count
		);
		Assert.Equal(1, second.MatchAll([MatchIl.Nop], IlPatternProvenanceConstraint.AllUnknown).Count);
		Assert.Equal(2, second.MatchAll([MatchIl.Nop], IlPatternProvenanceConstraint.Any).Count);
	}

	[Fact]
	public static void UniformProvenanceRequiresEveryMatchedInstructionToAgree() {
		IlMethodBody body = new BodyBuilder().Nop().Nop().Ret().Build();
		IlTransactionCore core = open(body);

		IlMatch match = core.MatchAll([MatchIl.Nop, MatchIl.Nop], IlPatternProvenanceConstraint.Any).RequireSingle();

		Assert.True(match.TryGetUniformProvenance(out IlProvenance provenance));
		Assert.Null(provenance.OwnerId);
	}
}
