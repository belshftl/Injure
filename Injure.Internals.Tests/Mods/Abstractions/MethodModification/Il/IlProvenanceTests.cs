// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Internals.Tests.Mods.Abstractions.MethodModification.Il;

public sealed class IlProvenanceTests {
	private const string otherOwner = "other";

	private static IlMethodBody baseline(int nops = 2) {
		BodyBuilder builder = new();
		for (int i = 0; i < nops; i++)
			builder.Nop();
		return builder.Ret().Build();
	}

	private static void stamp(IlMethodBody body, string ownerId, string localId, int count = 1) {
		IlTransactionCore core = new(body, ownerId, localId);
		core.EmitAtBoundary(0, e => {
			for (int i = 0; i < count; i++)
				e.Nop();
		});
		core.Commit();
	}

	private static int countMatches(IlMethodBody body, IlPatternProvenanceConstraint constraint, int length = 2) {
		IlTransactionCore core = new(body, IlTest.OwnerId, "reader");
		var pattern = new IlPatternElement[length];
		Array.Fill(pattern, MatchIl.Nop);
		return core.MatchAll(pattern, constraint).Count;
	}

	// ==========================================================================================
	// stamping
	[Fact]
	public void EmittedInstructionsCarryTheEmittingManipulator() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first");

		InternalIlProvenance provenance = body.Instructions[0].Provenance;
		Assert.Equal(IlTest.OwnerId, provenance.GetOwnerId());
		Assert.Equal("first", provenance.GetLocalId());
		Assert.False(provenance.IsUnknown);
	}

	[Fact]
	public void BaselineInstructionsKeepUnknownProvenanceAcrossEdits() {
		IlMethodBody body = baseline(1);
		stamp(body, IlTest.OwnerId, "first");
		stamp(body, otherOwner, "second");

		Assert.True(body.Instructions[^2].Provenance.IsUnknown); // the original nop
		Assert.True(body.Instructions[^1].Provenance.IsUnknown); // the original ret
	}

	[Fact]
	public void EarlierManipulatorsStampSurvivesLaterCommits() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first");
		stamp(body, otherOwner, "second");
		stamp(body, IlTest.OwnerId, "third");

		// each stamp prepends, so the last one emitted is first
		Assert.Equal("third", body.Instructions[0].Provenance.GetLocalId());
		Assert.Equal("second", body.Instructions[1].Provenance.GetLocalId());
		Assert.Equal("first", body.Instructions[2].Provenance.GetLocalId());
	}

	[Fact]
	public void ManipulatorCantSeeItsOwnEmissionsWithinOneTransaction() {
		IlMethodBody body = baseline(0);
		IlTransactionCore core = new(body, IlTest.OwnerId, "self");
		core.EmitAtBoundary(0, static e => { e.Nop(); e.Nop(); });
		Assert.Equal(0, core.MatchAll([MatchIl.Nop], IlPatternProvenanceConstraint.Any).Count);
		core.Commit();
	}

	// ==========================================================================================
	// AllFromOwner
	[Fact]
	public void AllFromOwnerSpansDifferentLocalIdsOfTheSameOwner() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first");
		stamp(body, IlTest.OwnerId, "second");

		Assert.NotEqual(
			body.Instructions[0].Provenance.GetLocalId(),
			body.Instructions[1].Provenance.GetLocalId()
		);
		Assert.Equal(1, countMatches(body, IlPatternProvenanceConstraint.AllFromOwner(IlTest.OwnerId)));
	}

	[Fact]
	public void AllFromOwnerRejectsRangeSpanningTwoOwners() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first");
		stamp(body, otherOwner, "second");

		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllFromOwner(IlTest.OwnerId)));
		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllFromOwner(otherOwner)));
	}

	[Fact]
	public void AllFromOwnerRejectsRangeIncludingUnknownProvenance() {
		IlMethodBody body = baseline(1);
		stamp(body, IlTest.OwnerId, "first");

		// instruction 0 is stamped, instruction 1 is the original nop
		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllFromOwner(IlTest.OwnerId)));
	}

	[Fact]
	public void AllFromOwnerFindsNothingForOwnerThatEmittedNothing() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first", count: 2);

		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllFromOwner(otherOwner)));
	}

	// ==========================================================================================
	// AllUniform and AllUnknown
	[Fact]
	public void AllUniformAcceptsOneOwnerAcrossSeveralManipulators() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first");
		stamp(body, IlTest.OwnerId, "second");

		Assert.Equal(1, countMatches(body, IlPatternProvenanceConstraint.AllUniform));
	}

	[Fact]
	public void AllUniformRejectsAllUnknownRange() {
		IlMethodBody body = baseline(2);

		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllUniform));
	}

	[Fact]
	public void AllUniformRejectsMixOfKnownAndUnknown() {
		IlMethodBody body = baseline(1);
		stamp(body, IlTest.OwnerId, "first");

		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllUniform));
	}

	[Fact]
	public void AllUniformRejectsTwoOwners() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first");
		stamp(body, otherOwner, "second");

		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllUniform));
	}

	[Fact]
	public void AllUnknownAcceptsOnlyUntouchedInstructions() {
		IlMethodBody body = baseline(2);
		Assert.Equal(1, countMatches(body, IlPatternProvenanceConstraint.AllUnknown));

		stamp(body, IlTest.OwnerId, "first");
		// the stamped nop now precedes the two originals, so exactly one all-unknown pair remains
		Assert.Equal(1, countMatches(body, IlPatternProvenanceConstraint.AllUnknown));
	}

	[Fact]
	public void AllUnknownRejectsRangeIncludingStampedInstruction() {
		IlMethodBody body = baseline(1);
		stamp(body, IlTest.OwnerId, "first");

		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllUnknown));
	}

	// ==========================================================================================
	// TryGetUniformProvenance, which intentionally disagrees a bit with AllUniform
	[Fact]
	public void TryGetUniformProvenanceTreatsAllUnknownAsUniform() {
		IlMethodBody body = baseline(2);
		IlTransactionCore core = new(body, IlTest.OwnerId, "reader");
		IlMatch match = core.MatchAll([MatchIl.Nop, MatchIl.Nop], IlPatternProvenanceConstraint.Any).RequireSingle();

		Assert.True(match.TryGetUniformProvenance(out IlProvenance provenance));
		Assert.Null(provenance.OwnerId);
		// the same range does not satisfy AllUniform
		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllUniform));
	}

	[Fact]
	public void TryGetUniformProvenanceReportsTheSharedOwner() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first");
		stamp(body, IlTest.OwnerId, "second");
		IlTransactionCore core = new(body, IlTest.OwnerId, "reader");
		IlMatch match = core.MatchAll([MatchIl.Nop, MatchIl.Nop], IlPatternProvenanceConstraint.Any).RequireSingle();

		Assert.True(match.TryGetUniformProvenance(out IlProvenance provenance));
		Assert.Equal(IlTest.OwnerId, provenance.OwnerId);
	}

	[Fact]
	public void TryGetUniformProvenanceFailsAcrossOwners() {
		IlMethodBody body = baseline(0);
		stamp(body, IlTest.OwnerId, "first");
		stamp(body, otherOwner, "second");
		IlTransactionCore core = new(body, IlTest.OwnerId, "reader");
		IlMatch match = core.MatchAll([MatchIl.Nop, MatchIl.Nop], IlPatternProvenanceConstraint.Any).RequireSingle();

		Assert.False(match.TryGetUniformProvenance(out _));
	}

	// ==========================================================================================
	// interning
	[Fact]
	public void EqualIdsInternToOneIndex() {
		InternalIlProvenance first = new(IlTest.OwnerId, "a");
		InternalIlProvenance second = new(string.Concat("te", "st"), "a");

		Assert.Equal(first.OwnerIdIndex, second.OwnerIdIndex);
		Assert.Equal(first, second);
	}

	[Fact]
	public void DifferentIdsInternToDifferentIndices() {
		InternalIlProvenance first = new(IlTest.OwnerId, "a");
		InternalIlProvenance second = new(otherOwner, "a");

		Assert.NotEqual(first.OwnerIdIndex, second.OwnerIdIndex);
		Assert.NotEqual(first, second);
	}

	[Fact]
	public void UnknownProvenanceIsIndexZero() {
		InternalIlProvenance unknown = default;

		Assert.Equal(0, unknown.OwnerIdIndex);
		Assert.Equal(0, unknown.LocalIdIndex);
		Assert.True(unknown.IsUnknown);
		Assert.Null(unknown.GetOwnerId());
		Assert.Null(unknown.GetLocalId());
	}

	[Fact]
	public void LocalIdsDoNotAffectOwnerComparison() {
		InternalIlProvenance first = new(IlTest.OwnerId, "a");
		InternalIlProvenance second = new(IlTest.OwnerId, "b");

		Assert.Equal(first.OwnerIdIndex, second.OwnerIdIndex);
		Assert.NotEqual(first.LocalIdIndex, second.LocalIdIndex);
		Assert.Equal(first.ToPublic(), second.ToPublic());
	}

	// ==========================================================================================
	// baseline provenance
	[Fact]
	public void BaselineProvenanceMarksEveryDecodedInstruction() {
		InternalIlProvenance baseline = new("game", null);
		IlMethodBody body = new BodyBuilder().Nop().Nop().Ret().Build(baselineProvenance: baseline);

		Assert.All(body.Instructions, i => Assert.Equal(baseline, i.Provenance));
		Assert.Equal(1, countMatches(body, IlPatternProvenanceConstraint.AllFromOwner("game")));
		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllUnknown));
		Assert.Equal(1, countMatches(body, IlPatternProvenanceConstraint.AllUniform));
	}

	[Fact]
	public void BaselineProvenanceIsDistinguishableFromAManipulatorsOwn() {
		InternalIlProvenance baseline = new("game", null);
		IlMethodBody body = new BodyBuilder().Nop().Ret().Build(baselineProvenance: baseline);
		stamp(body, IlTest.OwnerId, "patch");

		Assert.Equal(1, countMatches(body, IlPatternProvenanceConstraint.Any));
		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllFromOwner(IlTest.OwnerId)));
		Assert.Equal(0, countMatches(body, IlPatternProvenanceConstraint.AllFromOwner("game")));
		Assert.Equal(IlTest.OwnerId, body.Instructions[0].Provenance.GetOwnerId());
		Assert.Equal("game", body.Instructions[1].Provenance.GetOwnerId());
	}
}
