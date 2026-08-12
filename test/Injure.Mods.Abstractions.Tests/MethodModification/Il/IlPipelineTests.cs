// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Abstractions.Tests.MethodModification.Il;

public sealed class IlPipelineTests {
	private static IlMethodBody makeBaseline() => new BodyBuilder().Nop().Ret().Build();
	private static IlManipulatorRegistration makeManipulator(
		string localId,
		IlManipulator<TestL> manipulator,
		string ownerId = IlTest.OwnerId
	) => IlManipulatorRegistration.Create(ownerId, localId, manipulator);
	private static IlManipulatorRegistration noOp(string localId) => makeManipulator(localId, _ => { });
	private static IlManipulatorRegistration emitsNop(string localId) => makeManipulator(localId, ctx => ctx.EmitAtStart(e => e.Nop()));
	private static IlManipulatorRegistration emitsUnderflow(string localId) => makeManipulator(localId, ctx => ctx.EmitAtStart(e => e.Pop()));

	// ==========================================================================================
	// no-ops
	[Fact]
	public void NoManipulatorsReturnsBaseline() {
		IlMethodBody baseline = makeBaseline();
		IlPipelineResult result = IlPipeline.Transform(baseline, []);
		Assert.False(result.Modified);
		Assert.Same(baseline, result.Body);
	}

	[Fact]
	public void NoOpManipulatorsLeaveBodyUnmodified() {
		IlMethodBody baseline = makeBaseline();
		IlPipelineResult result = IlPipeline.Transform(baseline, [noOp("a"), noOp("b")]);
		Assert.False(result.Modified);
		Assert.Same(baseline, result.Body);
	}

	// ==========================================================================================
	// applying
	[Fact]
	public void EmittedInstructionsGoIntoCloneNotBaseline() {
		IlMethodBody baseline = makeBaseline();
		int before = baseline.Instructions.Count;
		IlPipelineResult result = IlPipeline.Transform(baseline, [emitsNop("a")]);
		Assert.True(result.Modified);
		Assert.NotSame(baseline, result.Body);
		Assert.Equal(before, baseline.Instructions.Count);
		Assert.Equal(before + 1, result.Body.Instructions.Count);
	}

	[Fact]
	public void ManipulatorsRunInGivenOrder() {
		List<int> order = new();
		IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("first", _ => order.Add(0)),
				makeManipulator("second", _ => order.Add(1)),
				makeManipulator("third", _ => order.Add(2)),
			]
		);
		Assert.Equal([0, 1, 2], order);
	}

	[Fact]
	public void LaterManipulatorsSeeEarlierEdits() {
		int observed = -1;
		IlPipeline.Transform(
			makeBaseline(),
			[emitsNop("first"), makeManipulator("second", ctx => observed = ctx.InstructionCount)]
		);
		Assert.Equal(3, observed);
	}

	[Fact]
	public void ValidationResultIsCachedOnTheReturnedBody() {
		IlPipelineResult result = IlPipeline.Transform(makeBaseline(), [emitsNop("a")]);
		Assert.NotNull(result.Body.ComputedMaxStack);
	}

	// ==========================================================================================
	// failure
	private sealed class ManipulatorException : Exception {
		public ManipulatorException() : base() {}
		public ManipulatorException(string msg) : base(msg) {}
	}

	[Fact]
	public void ManipulatorExceptionsBubbleOutUnchanged() {
		IlMethodBody baseline = makeBaseline();
		ManipulatorException thrown = new("from the manipulator");
		ManipulatorException caught = Assert.Throws<ManipulatorException>(
			() => IlPipeline.Transform(baseline, [makeManipulator("a", _ => throw thrown)])
		);
		Assert.Same(thrown, caught);
		Assert.Equal(2, baseline.Instructions.Count);
	}

	[Fact]
	public void FailedManipulatorEditsAreDiscarded() {
		IlMethodBody baseline = makeBaseline();
		Assert.Throws<ManipulatorException>(() => IlPipeline.Transform(
			baseline,
			[emitsNop("first"), makeManipulator("second", static _ => throw new ManipulatorException())]
		));
		Assert.Equal(2, baseline.Instructions.Count);
	}

	[Fact]
	public void SkipFailingManipulatorsDiscardsOnlyFailingManipulator() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("bad", ctx => {
					ctx.EmitAtStart(static e => {
						e.Nop();
						e.Nop();
					});
					throw new ManipulatorException();
				}),
				emitsNop("good"),
			],
			new IlPipelineOptions { SkipFailingManipulators = true }
		);
		Assert.True(result.Modified);
		Assert.Equal(3, result.Body.Instructions.Count);
	}

	[Fact]
	public void DuplicateIdentifiersAreRejected() =>
		Assert.ThrowsAny<Exception>(() => IlPipeline.Transform(makeBaseline(), [noOp("same"), noOp("same")]));

	[Fact]
	public void SameLocalIdCanExistInDifferentOwners() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[emitsNop("patch"), makeManipulator("patch", ctx => ctx.EmitAtStart(static e => e.Nop()), "test.other")]
		);
		Assert.Equal(4, result.Body.Instructions.Count);
	}

	// ==========================================================================================
	// validation and attribution
	[Fact]
	public void InvalidBodyIsAValidationFailure() {
		IlPipelineValidationException ex = Assert.Throws<IlPipelineValidationException>(
			static () => IlPipeline.Transform(makeBaseline(), [emitsUnderflow("bad")])
		);
		Assert.Equal(IlTest.OwnerId, ex.OwnerId);
		Assert.Equal("bad", ex.LocalId);
	}

	[Fact]
	public void AttributionNamesCulprit() {
		IlPipelineValidationException ex = Assert.Throws<IlPipelineValidationException>(
			static () => IlPipeline.Transform(makeBaseline(), [emitsNop("innocent"), emitsUnderflow("guilty"), emitsNop("later")])
		);
		Assert.Equal("guilty", ex.LocalId);
	}

	[Fact]
	public void AttributionRerunsEveryManipulatorExactlyOnceMore() {
		Dictionary<string, int> invocations = new();
		void count(string id) => invocations[id] = invocations.GetValueOrDefault(id) + 1;
		Assert.Throws<IlPipelineValidationException>(() => IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("first", ctx => { count("first"); ctx.EmitAtStart(e => e.Nop()); }),
				makeManipulator("bad", ctx => { count("bad"); ctx.EmitAtStart(e => e.Pop()); }),
			]
		));
		Assert.Equal(2, invocations["first"]);
		Assert.Equal(2, invocations["bad"]);
	}

	[Fact]
	public void AttributionCanBeDisabled() {
		int invocations = 0;
		IlPipelineValidationException ex = Assert.Throws<IlPipelineValidationException>(() => IlPipeline.Transform(
			makeBaseline(),
			[makeManipulator("bad", ctx => { invocations++; ctx.EmitAtStart(e => e.Pop()); })],
			new IlPipelineOptions { SkipFailureAttribution = true }
		));
		Assert.Null(ex.OwnerId);
		Assert.Null(ex.LocalId);
		Assert.Equal(1, invocations);
	}

	[Fact]
	public void DivergentRerunFallsBackToUnattributedFailure() {
		int invocations = 0;
		IlPipelineValidationException ex = Assert.Throws<IlPipelineValidationException>(() => IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("bad", ctx => {
					if (invocations++ == 0)
						ctx.EmitAtStart(static e => e.Pop());
					else
						throw new ManipulatorException("only on the second run");
				}),
			]
		));
		Assert.Null(ex.OwnerId);
	}

	[Fact]
	public void SkippingFinalValidationLeavesBodyUnanalyzed() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[emitsUnderflow("bad")],
			new IlPipelineOptions { SkipFinalValidation = true }
		);
		Assert.True(result.Modified);
		Assert.Null(result.Body.ComputedMaxStack);
	}

	[Fact]
	public void PerStepValidationFailsAtOffendingManipulator() {
		int ranAfter = 0;
		Assert.ThrowsAny<Exception>(() => IlPipeline.Transform(
			makeBaseline(),
			[emitsUnderflow("bad"), makeManipulator("after", _ => ranAfter++)],
			new IlPipelineOptions { ValidateAfterEachManipulator = true }
		));
		Assert.Equal(0, ranAfter);
	}
}
