// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Abstractions.Tests.Modif.Il;

public sealed class IlPipelineTests {
	private static IlMethodBody makeBaseline() => new BodyBuilder().Nop().Ret().Build();
	private static IlManipulatorRegistration makeManipulator(
		string localId,
		IlManipulator<TestL> manipulator,
		string ownerId = IlTest.OwnerId
	) => IlManipulatorRegistration.Create(ownerId, localId, manipulator);
	private static IlManipulatorRegistration noOp(string localId) => makeManipulator(localId, static _ => {});
	private static IlManipulatorRegistration emitsNop(string localId) => makeManipulator(localId, static ctx => ctx.EmitAtStart(static e => e.Nop()));
	private static IlManipulatorRegistration emitsUnderflow(string localId) => makeManipulator(localId, static ctx => ctx.EmitAtStart(static e => e.Pop()));

	// ==========================================================================================
	// no-ops
	[Fact]
	public static void NoManipulatorsReturnsBaseline() {
		IlMethodBody baseline = makeBaseline();
		IlPipelineResult result = IlPipeline.Transform(baseline, [], null, null);
		Assert.False(result.Modified);
		Assert.Same(baseline, result.Body);
	}

	[Fact]
	public static void NoOpManipulatorsLeaveBodyUnmodified() {
		IlMethodBody baseline = makeBaseline();
		IlPipelineResult result = IlPipeline.Transform(baseline, [noOp("a"), noOp("b")], null, null);
		Assert.False(result.Modified);
		Assert.Same(baseline, result.Body);
	}

	// ==========================================================================================
	// applying
	[Fact]
	public static void EmittedInstructionsGoIntoCloneNotBaseline() {
		IlMethodBody baseline = makeBaseline();
		int before = baseline.Instructions.Count;
		IlPipelineResult result = IlPipeline.Transform(baseline, [emitsNop("a")], null, null);
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
		IlPipelineResult result = IlPipeline.Transform(makeBaseline(), [emitsNop("a")], null, null);
		Assert.NotNull(result.Body.ComputedMaxStack);
	}

	// ==========================================================================================
	// failure
	[Fact]
	public static void ManipulatorExceptionsAreWrapped() {
		IlMethodBody baseline = makeBaseline();
		InvalidTimeZoneException thrown = new("from the manipulator");
		IlManipulatorException caught = Assert.Throws<IlManipulatorException>(
			() => IlPipeline.Transform(baseline, [makeManipulator("a", _ => throw thrown)], null, null)
		);
		Assert.Same(thrown, caught.InnerException);
		Assert.Equal(2, baseline.Instructions.Count);
	}

	[Fact]
	public static void FailedManipulatorEditsAreDiscarded() {
		IlMethodBody baseline = makeBaseline();
		Assert.Throws<IlManipulatorException>(() => IlPipeline.Transform(
			baseline,
			[emitsNop("first"), makeManipulator("second", static _ => throw new InvalidTimeZoneException())],
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
					throw new InvalidTimeZoneException();
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
		Assert.ThrowsAny<Exception>(() => IlPipeline.Transform(makeBaseline(), [noOp("same"), noOp("same")], null, null));

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
			static () => IlPipeline.Transform(makeBaseline(), [emitsUnderflow("bad")], null, null)
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
						throw new InvalidTimeZoneException("only on the second run");
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

	// ==========================================================================================
	// locals
	private sealed class FixedOwnerCtx(IlOwnerCtx ctx) : IIlOwnerCtxProvider {
		public IlOwnerCtx GetContext(string ownerId) => ctx;
	}

	private const string reloadableAssembly = "Reloadable";

	private static readonly IIlOwnerCtxProvider reloadableMod = new FixedOwnerCtx(new IlOwnerCtx(
		new Dictionary<string, (string, bool)> { [reloadableAssembly] = ("reloadable", true) },
		new HashSet<string>()
	));

	private static IlNamedTypeRef reloadableStruct() => new(
		new IlTypeScope.Assembly(new IlAssemblyIdentity(reloadableAssembly, null, null, [], default)),
		null,
		"Mod",
		"Struct",
		0,
		IlNamedTypeKind.ValueType
	);

	/// <summary>
	/// Declares a local, stores to it at the start, and loads it back after the baseline's <c>nop</c>,
	/// so the local is used from two separate fragments.
	/// </summary>
	private static IlManipulatorRegistration declaresAndUses(string localId, Func<IlCtx<TestL>, IlLocal> declare, string ownerId = IlTest.OwnerId) =>
		makeManipulator(localId, ctx => {
			IlLocal local = declare(ctx);
			ctx.EmitAtStart(e => { e.LdcI4(1); e.Stloc(local); });
			ctx.MatchNext([MatchIl.Nop], IlProvenanceConstr.Any).EmitAfter(e => { e.Ldloc(local); e.Pop(); });
		}, ownerId);

	private static int localIndexOf(IlInstruction instr) => Assert.IsType<IlLocalOperand>(instr.Operand).Index;

	[Fact]
	public static void ADeclaredLocalIsUsableAcrossFragments() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[declaresAndUses("a", static ctx => ctx.DeclareLocal(IlTest.Int32))],
			null,
			null
		);

		Assert.Equal(new IlTypeRef[] { IlTest.Int32 }, result.Body.Locals.ToArray());
		IlInstruction[] uses = result.Body.Instructions.Where(static i => i.Operand is IlLocalOperand).ToArray();
		Assert.Equal([ILOpCode.Stloc, ILOpCode.Ldloc], uses.Select(static i => i.OpCode).ToArray());
		Assert.All(uses, static i => Assert.Equal(0, localIndexOf(i)));
		Assert.NotNull(result.Body.ComputedMaxStack);
	}

	[Fact]
	public static void EachManipulatorsLocalsAreAppendedAfterEarlierOnes() {
		List<int> indices = new();
		IlPipelineResult result = IlPipeline.Transform(
			new BodyBuilder().Nop().Ret().Build(locals: [IlTest.Object]),
			[
				declaresAndUses("first", ctx => { IlLocal l = ctx.DeclareLocal(IlTest.Int32); indices.Add(l.Index); return l; }),
				declaresAndUses("second", ctx => { IlLocal l = ctx.DeclareLocal(IlTest.Int32); indices.Add(l.Index); return l; }),
			],
			null,
			null
		);

		Assert.Equal([1, 2], indices);
		Assert.Equal(3, result.Body.Locals.Length);
		Assert.Equal(
			new[] { default, new InternalIlProvenance(IlTest.OwnerId, "first"), new InternalIlProvenance(IlTest.OwnerId, "second") },
			result.Body.LocalsProvenance.ToArray()
		);
	}

	[Fact]
	public static void ALocallessBaselineBecomesZeroing() {
		IlPipelineResult result = IlPipeline.Transform(
			new BodyBuilder().Nop().Ret().Build(initLocals: false),
			[declaresAndUses("a", static ctx => ctx.DeclareLocal(IlTest.Int32))],
			null,
			null
		);

		Assert.True(result.Body.InitLocals);
	}

	[Fact]
	public static void ALocalFromAManipulatorThatEmitsNothingIsDropped() {
		IlMethodBody baseline = makeBaseline();
		IlPipelineResult result = IlPipeline.Transform(
			baseline,
			[makeManipulator("a", static ctx => ctx.DeclareLocal(IlTest.Int32))],
			null,
			null
		);

		Assert.False(result.Modified);
		Assert.Same(baseline, result.Body);
		Assert.Empty(result.Body.Locals);
	}

	[Fact]
	public static void ALocalCantBeUsedByALaterManipulator() {
		IlLocal leaked = default;
		IlManipulatorException ex = Assert.Throws<IlManipulatorException>(() => IlPipeline.Transform(
			makeBaseline(),
			[
				declaresAndUses("declares", ctx => leaked = ctx.DeclareLocal(IlTest.Int32)),
				makeManipulator("reuses", ctx => ctx.EmitAtStart(e => { e.Ldloc(leaked); e.Pop(); })),
			],
			null,
			null
		));

		Assert.Equal("reuses", ex.LocalId);
		Assert.IsType<IlPipelineException>(ex.InnerException);
	}

	[Fact]
	public static void AFailedManipulatorsLocalsAreDiscarded() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[
				makeManipulator("bad", static ctx => {
					IlLocal local = ctx.DeclareLocal(IlTest.Object);
					ctx.EmitAtStart(e => { e.Ldnull(); e.Stloc(local); });
					throw new InvalidTimeZoneException();
				}),
				declaresAndUses("good", static ctx => ctx.DeclareLocal(IlTest.Int32)),
			],
			null,
			null,
			new IlPipelineOptions { SkipFailingManipulators = true }
		);

		Assert.Equal(new IlTypeRef[] { IlTest.Int32 }, result.Body.Locals.ToArray());
		Assert.All(result.Body.Instructions.Where(static i => i.Operand is IlLocalOperand), static i => Assert.Equal(0, localIndexOf(i)));
	}

	[Fact]
	public static void AVoidLocalIsRejectedWithAttribution() {
		IlManipulatorException ex = Assert.Throws<IlManipulatorException>(() => IlPipeline.Transform(
			makeBaseline(),
			[makeManipulator("bad", static ctx => ctx.DeclareLocal(IlRefFactory.Type(typeof(void))))],
			null,
			null
		));

		Assert.Equal("bad", ex.LocalId);
		Assert.IsType<ArgumentException>(ex.InnerException);
	}

	[Fact]
	public static void AReloadableValueTypeLocalIsRejectedWithAttribution() {
		IlManipulatorException ex = Assert.Throws<IlManipulatorException>(() => IlPipeline.Transform(
			makeBaseline(),
			[makeManipulator("bad", static ctx => ctx.DeclareLocal(reloadableStruct()))],
			reloadableMod,
			null
		));

		Assert.Equal("bad", ex.LocalId);
		Assert.IsType<IlCollectibleReferenceException>(ex.InnerException);
	}

	[Fact]
	public static void AByrefToAReloadableValueTypeIsAccepted() {
		IlPipelineResult result = IlPipeline.Transform(
			makeBaseline(),
			[makeManipulator("a", static ctx => {
				IlLocal local = ctx.DeclareLocal(IlRefFactory.ByRef(reloadableStruct()));
				ctx.EmitAtStart(e => { e.Ldloc(local); e.Pop(); });
			})],
			reloadableMod,
			null
		);

		Assert.IsType<IlByRefTypeRef>(Assert.Single(result.Body.Locals));
	}
}
