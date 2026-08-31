// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Abstractions.Tests.Modif.Il;

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
	public static void NoManipulatorsReturnsBaseline() {
		IlMethodBody baseline = makeBaseline();
		IlPipelineResult result = IlPipeline.Transform(baseline, [], default, null);
		Assert.False(result.Modified);
		Assert.Same(baseline, result.Body);
	}

	[Fact]
	public static void NoOpManipulatorsLeaveBodyUnmodified() {
		IlMethodBody baseline = makeBaseline();
		IlPipelineResult result = IlPipeline.Transform(baseline, [noOp("a"), noOp("b")], default, null);
		Assert.False(result.Modified);
		Assert.Same(baseline, result.Body);
	}

	// ==========================================================================================
	// applying
	[Fact]
	public static void EmittedInstructionsGoIntoCloneNotBaseline() {
		IlMethodBody baseline = makeBaseline();
		int before = baseline.Instructions.Count;
		IlPipelineResult result = IlPipeline.Transform(baseline, [emitsNop("a")], default, null);
		Assert.True(result.Modified);
		Assert.NotSame(baseline, result.Body);
		Assert.Equal(before, baseline.Instructions.Count);
		Assert.Equal(before + 1, result.Body.Instructions.Count);
	}

	[Fact]
	public static void ManipulatorsRunInGivenOrder() {
		List<int> order = new();
		IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("first", _ => order.Add(0)),
				makeManipulator("second", _ => order.Add(1)),
				makeManipulator("third", _ => order.Add(2)),
			],
			default,
			null
		);
		Assert.Equal([0, 1, 2], order);
	}

	[Fact]
	public static void LaterManipulatorsSeeEarlierEdits() {
		int observed = -1;
		IlPipeline.Transform(
			makeBaseline(),
			[emitsNop("first"), makeManipulator("second", ctx => observed = ctx.InstructionCount)],
			default,
			null
		);
		Assert.Equal(3, observed);
	}

	[Fact]
	public static void ValidationResultIsCachedOnTheReturnedBody() {
		IlPipelineResult result = IlPipeline.Transform(makeBaseline(), [emitsNop("a")], default, null);
		Assert.NotNull(result.Body.ComputedMaxStack);
	}

	// ==========================================================================================
	// failure
	private sealed class OtherException : Exception {
		public OtherException() : base() {}
		public OtherException(string msg) : base(msg) {}
	}

	[Fact]
	public static void ManipulatorExceptionsAreWrapped() {
		IlMethodBody baseline = makeBaseline();
		OtherException thrown = new("from the manipulator");
		IlManipulatorException caught = Assert.Throws<IlManipulatorException>(
			() => IlPipeline.Transform(baseline, [makeManipulator("a", _ => throw thrown)], default, null)
		);
		Assert.Same(thrown, caught.InnerException);
		Assert.Equal(2, baseline.Instructions.Count);
	}

	[Fact]
	public static void FailedManipulatorEditsAreDiscarded() {
		IlMethodBody baseline = makeBaseline();
		Assert.Throws<IlManipulatorException>(() => IlPipeline.Transform(
			baseline,
			[emitsNop("first"), makeManipulator("second", static _ => throw new OtherException())],
			default,
			null
		));
		Assert.Equal(2, baseline.Instructions.Count);
	}

	[Fact]
	public static void SkipFailingManipulatorsDiscardsOnlyFailingManipulator() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("bad", ctx => {
					ctx.EmitAtStart(static e => {
						e.Nop();
						e.Nop();
					});
					throw new OtherException();
				}),
				emitsNop("good"),
			],
			default,
			null,
			new IlPipelineOptions { SkipFailingManipulators = true }
		);
		Assert.True(result.Modified);
		Assert.Equal(3, result.Body.Instructions.Count);
	}

	[Fact]
	public static void DuplicateIdentifiersAreRejected() =>
		Assert.ThrowsAny<Exception>(() => IlPipeline.Transform(makeBaseline(), [noOp("same"), noOp("same")], default, null));

	[Fact]
	public static void SameLocalIdCanExistInDifferentOwners() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[emitsNop("patch"), makeManipulator("patch", ctx => ctx.EmitAtStart(static e => e.Nop()), "test.other")],
			default,
			null
		);
		Assert.Equal(4, result.Body.Instructions.Count);
	}

	// ==========================================================================================
	// validation and attribution
	[Fact]
	public static void InvalidBodyIsAValidationFailure() {
		IlPipelineValidationException ex = Assert.Throws<IlPipelineValidationException>(
			static () => IlPipeline.Transform(makeBaseline(), [emitsUnderflow("bad")], default, null)
		);
		Assert.Equal(IlTest.OwnerId, ex.OwnerId);
		Assert.Equal("bad", ex.LocalId);
	}

	[Fact]
	public static void AttributionNamesCulprit() {
		IlPipelineValidationException ex = Assert.Throws<IlPipelineValidationException>(
			static () => IlPipeline.Transform(
				makeBaseline(),
				[emitsNop("innocent"), emitsUnderflow("guilty"), emitsNop("later")],
				default,
				null
			)
		);
		Assert.Equal("guilty", ex.LocalId);
	}

	[Fact]
	public static void AttributionRerunsEveryManipulatorExactlyOnceMore() {
		Dictionary<string, int> invocations = new();
		void count(string id) => invocations[id] = invocations.GetValueOrDefault(id) + 1;
		Assert.Throws<IlPipelineValidationException>(() => IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("first", ctx => { count("first"); ctx.EmitAtStart(e => e.Nop()); }),
				makeManipulator("bad", ctx => { count("bad"); ctx.EmitAtStart(e => e.Pop()); }),
			],
			default,
			null
		));
		Assert.Equal(2, invocations["first"]);
		Assert.Equal(2, invocations["bad"]);
	}

	[Fact]
	public static void AttributionCanBeDisabled() {
		int invocations = 0;
		IlPipelineValidationException ex = Assert.Throws<IlPipelineValidationException>(() => IlPipeline.Transform(
			makeBaseline(),
			[makeManipulator("bad", ctx => { invocations++; ctx.EmitAtStart(e => e.Pop()); })],
			default,
			null,
			new IlPipelineOptions { SkipFailureAttribution = true }
		));
		Assert.Null(ex.OwnerId);
		Assert.Null(ex.LocalId);
		Assert.Equal(1, invocations);
	}

	[Fact]
	public static void DivergentRerunFallsBackToUnattributedFailure() {
		int invocations = 0;
		IlPipelineValidationException ex = Assert.Throws<IlPipelineValidationException>(() => IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("bad", ctx => {
					if (invocations++ == 0)
						ctx.EmitAtStart(static e => e.Pop());
					else
						throw new OtherException("only on the second run");
				}),
			],
			default,
			null
		));
		Assert.Null(ex.OwnerId);
	}

	[Fact]
	public static void SkippingFinalValidationLeavesBodyUnanalyzed() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[emitsUnderflow("bad")],
			default,
			null,
			new IlPipelineOptions { SkipFinalValidation = true }
		);
		Assert.True(result.Modified);
		Assert.Null(result.Body.ComputedMaxStack);
	}

	[Fact]
	public static void PerStepValidationFailsAtOffendingManipulator() {
		int ranAfter = 0;
		Assert.ThrowsAny<Exception>(() => IlPipeline.Transform(
			makeBaseline(),
			[emitsUnderflow("bad"), makeManipulator("after", _ => ranAfter++)],
			default,
			null,
			new IlPipelineOptions { ValidateAfterEachManipulator = true }
		));
		Assert.Equal(0, ranAfter);
	}
}
