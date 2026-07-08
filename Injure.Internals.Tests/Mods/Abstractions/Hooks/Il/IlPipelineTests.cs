// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Hooks.Il;

using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Injure.Internals.Tests.Mods.Abstractions.Hooks.Il;

public sealed class IlPipelineTests {
	[ModLifetimeIdentityBelongsTo("IlPipelineTests")]
	private readonly struct TestL : IModLifetimeIdentity;

	[Fact]
	public void EmitsAtStartAndEnd() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));

		IlPipelineResult result = transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				ctx.EmitAtStart(static e => {
					e.LdcI4(1);
					e.Pop();
				});
				ctx.EmitAtEnd(static e => e.Nop());
			})
		);

		assertCodes(result.Method, Code.Ldc_I4_1, Code.Pop, Code.Ret, Code.Nop);
	}

	[Fact]
	public void MatchAllAndRequireSingleWork() {
		MethodDefinition method = createVoidMethod(
			Instruction.Create(OpCodes.Ldc_I4_1),
			Instruction.Create(OpCodes.Pop),
			Instruction.Create(OpCodes.Ldc_I4_2),
			Instruction.Create(OpCodes.Pop),
			Instruction.Create(OpCodes.Ret)
		);

		transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				IlMatches pops = ctx.MatchAll(
					[MatchIl.Pop],
					IlPatternProvenanceConstraint.AllFromOwner("game")
				);
				Assert.Equal(2, pops.Count);

				IlMatch ldcI4Pop = ctx.MatchAll(
					[MatchIl.LdcI4(2), MatchIl.Pop],
					IlPatternProvenanceConstraint.AllFromOwner("game")
				).RequireSingle();
				Assert.Equal(2, ldcI4Pop.InstructionCount);
			})
		);
	}

	[Fact]
	public void MatchPrevFindsNearestPreviousMatch() {
		MethodDefinition method = createVoidMethod(
			Instruction.Create(OpCodes.Ldc_I4_1),
			Instruction.Create(OpCodes.Pop),
			Instruction.Create(OpCodes.Ldc_I4_2),
			Instruction.Create(OpCodes.Pop),
			Instruction.Create(OpCodes.Ret)
		);

		transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				IlMatch ret = ctx.MatchNext(
					[MatchIl.Ret],
					IlPatternProvenanceConstraint.AllFromOwner("game")
				);
				IlMatch previous = ret.MatchPrev(
					[MatchIl.LdcI4(2), MatchIl.Pop],
					IlPatternProvenanceConstraint.AllFromOwner("game")
				);
				Assert.Equal(2, previous.InstructionCount);
			})
		);
	}

	[Fact]
	public void ManipulatorDoesntSeeItsOwnPendingInsertions() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		bool caughtIlMatchException = false;

		transform(
			method,
			"game",
			registration("mod", "hook", ctx => {
				ctx.EmitAtStart(static e => {
					e.LdcI4(7);
					e.Pop();
				});
				try {
					ctx.MatchNext(
						[MatchIl.LdcI4(7), MatchIl.Pop],
						IlPatternProvenanceConstraint.Any
					);
				} catch (IlMatchException) {
					caughtIlMatchException = true;
				}
			})
		);

		Assert.True(caughtIlMatchException);
		assertCodes(method, Code.Ldc_I4_7, Code.Pop, Code.Ret);
	}

	[Fact]
	public void LaterManipulatorSeesEarlierManipulatorOutput() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));

		transform(
			method,
			"game",
			registration("first-mod", "hook", static ctx => {
				ctx.EmitAtStart(static e => {
					e.LdcI4(1);
					e.Pop();
				});
			}),
			registration("second-mod", "hook", static ctx => {
				IlMatch firstInsertion = ctx.MatchNext(
					[MatchIl.LdcI4(1), MatchIl.Pop],
					IlPatternProvenanceConstraint.AllFromOwner("first-mod")
				);
				firstInsertion.EmitAfter(static e => {
					e.LdcI4(2);
					e.Pop();
				});
			})
		);

		assertCodes(method, Code.Ldc_I4_1, Code.Pop, Code.Ldc_I4_2, Code.Pop, Code.Ret);
	}

	[Fact]
	public void ForwardLabelResolvesToLaterEmittedInstruction() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));

		transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				IlLabel target = ctx.DefineLabel();
				ctx.EmitAtStart(e => {
					e.Br(target);
					e.LdcI4(99);
					e.Pop();
				});
				ctx.EmitAtEnd(e => {
					e.MarkLabel(target);
					e.Nop();
				});
			})
		);

		assertCodes(method, Code.Br, Code.Ldc_I4_S, Code.Pop, Code.Ret, Code.Nop);
		Instruction branch = method.Body.Instructions[0];
		Assert.Same(method.Body.Instructions[4], branch.Operand);
	}

	[Fact]
	public void TrailingLabelCreatesAnchorInstruction() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));

		transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				IlLabel end = ctx.DefineLabel();
				ctx.EmitAtStart(e => e.Br(end));
				ctx.EmitAtEnd(e => e.MarkLabel(end));
			})
		);

		assertCodes(method, Code.Br, Code.Ret, Code.Nop);
		Assert.Same(method.Body.Instructions[2], method.Body.Instructions[0].Operand);
	}

	[Fact]
	public void MultipleInsertionsAtOneBoundaryAreInDeclarationOrder() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));

		transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				ctx.EmitAtStart(static e => {
					e.LdcI4(1);
					e.Pop();
				});
				ctx.EmitAtStart(static e => {
					e.LdcI4(2);
					e.Pop();
				});
			})
		);

		assertCodes(method, Code.Ldc_I4_1, Code.Pop, Code.Ldc_I4_2, Code.Pop, Code.Ret);
	}

	[Fact]
	public void ProvenanceConstraintAvoidsMatchingEarlierHookOutput() {
		MethodDefinition method = createVoidMethod(
			Instruction.Create(OpCodes.Ldc_I4_1),
			Instruction.Create(OpCodes.Pop),
			Instruction.Create(OpCodes.Ret)
		);
		int gameMatches = -1;

		transform(
			method,
			"game",
			registration("first-mod", "hook", static ctx => {
				ctx.EmitAtStart(static e => {
					e.LdcI4(1);
					e.Pop();
				});
			}),
			registration("second-mod", "hook", ctx => {
				IlMatches matches = ctx.MatchAll(
					[MatchIl.LdcI4(1), MatchIl.Pop],
					IlPatternProvenanceConstraint.AllFromOwner("game")
				);
				gameMatches = matches.Count;
			})
		);

		Assert.Equal(1, gameMatches);
	}


	[Fact]
	public void CallPatternMatches() {
		MethodDefinition target = createVoidMethod(Instruction.Create(OpCodes.Ret));
		target.Name = "Target";
		MethodDefinition method = createVoidMethod(
			Instruction.Create(OpCodes.Call, target),
			Instruction.Create(OpCodes.Ret)
		);
		bool matched = false;

		transform(
			method,
			"game",
			registration("mod", "hook", ctx => {
				IlMatch call = ctx.MatchNext(
					[MatchIl.Call(target)], 
					IlPatternProvenanceConstraint.AllFromOwner("game")
				);
				matched = call.InstructionCount == 1;
			})
		);

		Assert.True(matched);
	}

	[Fact]
	public void MatchRequiresProvenanceConstraint() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		bool defaultRejected = false;

		transform(
			method,
			"game",
			registration("mod", "hook", ctx => {
				Assert.Throws<ArgumentException>(() => {
					ctx.MatchAll([MatchIl.Any], default);
				});
				defaultRejected = true;
			})
		);

		Assert.True(defaultRejected);
	}

	[Fact]
	public void AllUniformRequiresKnownProvenance() {
		MethodDefinition unknownMethod = createVoidMethod(Instruction.Create(OpCodes.Ret));
		int uniformMatches = -1;
		int unknownMatches = -1;

		transform(
			unknownMethod,
			null,
			registration("mod", "hook", ctx => {
				uniformMatches = ctx.MatchAll(
					[MatchIl.Ret],
					IlPatternProvenanceConstraint.AllUniform
				).Count;
				unknownMatches = ctx.MatchAll(
					[MatchIl.Ret],
					IlPatternProvenanceConstraint.AllUnknown
				).Count;
			})
		);

		Assert.Equal(0, uniformMatches);
		Assert.Equal(1, unknownMatches);

		MethodDefinition knownMethod = createVoidMethod(Instruction.Create(OpCodes.Ret));
		transform(
			knownMethod,
			"game",
			registration("mod", "hook", ctx => {
				uniformMatches = ctx.MatchAll(
					[MatchIl.Ret],
					IlPatternProvenanceConstraint.AllUniform
				).Count;
			})
		);

		Assert.Equal(1, uniformMatches);
	}

	[Fact]
	public void MatchCanReportUniformAndMixedProvenance() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		bool uniform = false;
		bool mixed = false;

		transform(
			method,
			"game",
			registration("first-mod", "hook", static ctx => ctx.EmitAtStart(static e => e.Nop())),
			registration("second-mod", "hook", ctx => {
				IlMatch inserted = ctx.MatchNext(
					[MatchIl.Nop],
					IlPatternProvenanceConstraint.AllFromOwner("first-mod")
				);
				uniform = inserted.TryGetUniformProvenance(out IlProvenance p) && p.OwnerId == "first-mod";

				IlMatch whole = ctx.MatchNext(
					[MatchIl.Nop, MatchIl.Ret],
					IlPatternProvenanceConstraint.Any
				);
				mixed = !whole.TryGetUniformProvenance(out _);
			})
		);

		Assert.True(uniform);
		Assert.True(mixed);
	}

	[Fact]
	public void ReferenceImportsCanImportFieldsForMatchingAndEmission() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		FieldInfo fieldInfo = typeof(FieldHolder).GetField(nameof(FieldHolder.StaticValue))!;
		bool matched = false;

		transform(
			method,
			"game",
			registration("first-mod", "hook", ctx => {
				FieldReference field = ctx.Imports.Import(fieldInfo);
				ctx.EmitAtStart(e => {
					e.Ldsfld(field);
					e.Pop();
				});
			}),
			registration("second-mod", "hook", ctx => {
				FieldReference field = ctx.Imports.Import(fieldInfo);
				IlMatch m = ctx.MatchNext(
					[MatchIl.Ldsfld(field)],
					IlPatternProvenanceConstraint.AllFromOwner("first-mod")
				);
				matched = m.InstructionCount == 1;
			})
		);

		Assert.True(matched);
		assertCodes(method, Code.Ldsfld, Code.Pop, Code.Ret);
		FieldReference emittedField = Assert.IsType<FieldReference>(method.Body.Instructions[0].Operand);
		Assert.Equal(nameof(FieldHolder.StaticValue), emittedField.Name);
	}

	[Fact]
	public void FieldEmitterHelpersEmitExpectedOpcodes() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		FieldInfo staticFieldInfo = typeof(FieldHolder).GetField(nameof(FieldHolder.StaticValue))!;
		FieldInfo instanceFieldInfo = typeof(FieldHolder).GetField(nameof(FieldHolder.InstanceValue))!;

		transform(
			method,
			"game",
			registration("mod", "hook", ctx => {
				FieldReference staticField = ctx.Imports.Import(staticFieldInfo);
				FieldReference instanceField = ctx.Imports.Import(instanceFieldInfo);
				ctx.EmitAtStart(e => {
					e.Ldfld(instanceField);
					e.Ldsfld(staticField);
					e.Stfld(instanceField);
					e.Stsfld(staticField);
					e.Ldflda(instanceField);
					e.Ldsflda(staticField);
				});
			})
		);

		assertCodes(method, Code.Ldfld, Code.Ldsfld, Code.Stfld, Code.Stsfld, Code.Ldflda, Code.Ldsflda, Code.Ret);
		for (int i = 0; i < 6; i++)
			Assert.IsType<FieldReference>(method.Body.Instructions[i].Operand);
	}

	[Fact]
	public void ExistingBranchTargetRemainsOnOriginalInstruction() {
		var target = Instruction.Create(OpCodes.Ret);
		MethodDefinition method = createVoidMethod(
			Instruction.Create(OpCodes.Br, target),
			target
		);

		transform(
			method,
			"game",
			registration("mod", "hook", static ctx => ctx.EmitAtStart(static e => e.Nop()))
		);

		assertCodes(method, Code.Nop, Code.Br, Code.Ret);
		Assert.Same(method.Body.Instructions[2], method.Body.Instructions[1].Operand);
	}

	[Fact]
	public void ExistingExceptionHandlerBoundaryRemainsOnOriginalInstruction() {
		var tryStart = Instruction.Create(OpCodes.Nop);
		var leave = Instruction.Create(OpCodes.Leave, Instruction.Create(OpCodes.Ret));
		var handlerStart = Instruction.Create(OpCodes.Pop);
		var handlerLeave = Instruction.Create(OpCodes.Leave, leave.Operand as Instruction);
		var end = (Instruction)leave.Operand;
		MethodDefinition method = createVoidMethod(tryStart, leave, handlerStart, handlerLeave, end);
		method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) {
			CatchType = method.Module.ImportReference(typeof(Exception)),
			TryStart = tryStart,
			TryEnd = handlerStart,
			HandlerStart = handlerStart,
			HandlerEnd = end,
		});

		transform(
			method,
			"game",
			registration("mod", "hook", static ctx => ctx.EmitAtStart(static e => e.Nop()))
		);

		assertCodes(method, Code.Nop, Code.Nop, Code.Leave, Code.Pop, Code.Leave, Code.Ret);
		ExceptionHandler handler = Assert.Single(method.Body.ExceptionHandlers);
		Assert.Same(method.Body.Instructions[1], handler.TryStart);
		Assert.Same(method.Body.Instructions[3], handler.HandlerStart);
	}

	[Fact]
	public void EmitCallbackFailureDoesntPublishPartialFragment() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		MethodBody originalBody = method.Body;

		Assert.Throws<IlPipelineException>(() => transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				ctx.EmitAtStart(static e => {
					e.Nop();
					throw new Exception();
				});
			})
		));

		Assert.Same(originalBody, method.Body);
		assertCodes(method, Code.Ret);
	}

	[Fact]
	public void LaterManipulatorFailureLeavesOriginalBodyUnchanged() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		MethodBody originalBody = method.Body;
		Instruction originalInstruction = method.Body.Instructions[0];

		Assert.Throws<IlPipelineException>(() => transform(
			method,
			"game",
			registration("first-mod", "hook", static ctx => ctx.EmitAtStart(static e => e.Nop())),
			registration("second-mod", "hook", static ctx => throw new Exception())
		));

		Assert.Same(originalBody, method.Body);
		Assert.Same(originalInstruction, method.Body.Instructions[0]);
		assertCodes(method, Code.Ret);
	}

	[Fact]
	public void ReferencedUnmarkedLabelThrows() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));

		IlPipelineException ex = Assert.Throws<IlPipelineException>(() => transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				IlLabel target = ctx.DefineLabel();
				ctx.EmitAtStart(e => e.Br(target));
			})
		));

		Assert.Contains("never marked", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void DuplicateLabelMarkThrows() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));

		IlPipelineException ex = Assert.Throws<IlPipelineException>(() => transform(
			method,
			"game",
			registration("mod", "hook", static ctx => {
				IlLabel label = ctx.DefineLabel();
				ctx.EmitAtStart(e => e.MarkLabel(label));
				ctx.EmitAtEnd(e => e.MarkLabel(label));
			})
		));

		Assert.Contains("marked more than once", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void LabelFromAnotherManipulatorIsRejected() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		IlLabel label = default;

		IlPipelineException ex = Assert.Throws<IlPipelineException>(() => transform(
			method,
			"game",
			registration("first-mod", "hook", ctx => {
				label = ctx.DefineLabel();
			}),
			registration("second-mod", "hook", ctx => {
				ctx.EmitAtStart(e => e.Br(label));
			})
		));

		Assert.Contains("another manipulation transaction", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void DuplicateManipulatorIdentityThrows() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		IlManipulatorRegistration first = registration("mod", "same", static ctx => { });
		IlManipulatorRegistration second = registration("mod", "same", static ctx => { });

		IlPipelineException ex = Assert.Throws<IlPipelineException>(() => transform(method, "game", first, second));
		Assert.Contains("duplicate manipulator identity", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void EmptyPatternThrows() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));

		Assert.Throws<IlPipelineException>(() => transform(
			method,
			"game",
			registration("mod", "hook", static ctx => ctx.MatchAll([], IlPatternProvenanceConstraint.Any))
		));
	}


	[Fact]
	public void ManagedDelegateEmissionUsesLowererAndTransfersRetention() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		RecordingManagedDelegateLowerer lowerer = new();
		Action callback = static () => { };

		IlPipelineResult result = transform(
			method,
			"game",
			lowerer,
			registration("mod", "hook", ctx => {
				ctx.EmitAtStart(e => e.Delegate(callback));
			})
		);

		assertCodes(method, Code.Call, Code.Ret);
		ManagedDelegateRequestRecord request = Assert.Single(lowerer.Requests);
		Assert.Same(method, request.Method);
		Assert.Same(callback, request.Callback);
		Assert.Equal("mod", request.OwnerId);
		Assert.Equal("hook", request.LocalId);
		Assert.Equal("mod", result.GetProvenance(method.Body.Instructions[0]).OwnerId);
		Assert.Equal(0, lowerer.DisposalCount);

		result.Dispose();
		Assert.Equal(1, lowerer.DisposalCount);
		result.Dispose();
		Assert.Equal(1, lowerer.DisposalCount);
	}

	[Fact]
	public void ManagedDelegateEmissionWithoutLowererFailsAtomically() {
		static void callback() {}
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		MethodBody originalBody = method.Body;

		InternalStateException ex = Assert.Throws<InternalStateException>(() => transform(
			method,
			"game",
			registration("mod", "hook", ctx => {
				ctx.EmitAtStart(e => e.Delegate(callback));
			})
		));

		Assert.Contains("no managed-delegate lowerer", ex.Message, StringComparison.Ordinal);
		Assert.Same(originalBody, method.Body);
		assertCodes(method, Code.Ret);
	}

	[Fact]
	public void PipelineFailureDisposesManagedDelegateRetentions() {
		static void callback() {}
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		MethodBody originalBody = method.Body;
		RecordingManagedDelegateLowerer lowerer = new();

		Assert.Throws<IlPipelineException>(() => transform(
			method,
			"game",
			lowerer,
			registration("first-mod", "hook", ctx => {
				ctx.EmitAtStart(e => e.Delegate(callback));
			}),
			registration("second-mod", "hook", static ctx => throw new Exception())
		));

		Assert.Equal(1, lowerer.DisposalCount);
		Assert.Same(originalBody, method.Body);
		assertCodes(method, Code.Ret);
	}

	[Fact]
	public void CapturedIlContextIsInvalidAfterManipulatorReturns() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		IlContext<TestL>? captured = null;

		transform(
			method,
			"game",
			registration("mod", "hook", ctx => {
				captured = ctx;
			})
		);

		Assert.NotNull(captured);
		Assert.Throws<IlTransactionExpiredException>(() => _ = captured.InstructionCount);
		Assert.Throws<IlTransactionExpiredException>(() => captured.EmitAtStart(static e => e.Nop()));
	}

	[Fact]
	public void DroppedManipulatorRegistrationCantBeUsed() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		IlManipulatorRegistration r = registration("mod", "hook", static ctx => ctx.EmitAtStart(static e => e.Nop()));
		r.DropStrongReferences();
		InternalStateException ex = Assert.Throws<InternalStateException>(() => transform(method, "game", r));
	}

	[Fact]
	public void DroppedPipelineResultCantBeUsed() {
		MethodDefinition method = createVoidMethod(Instruction.Create(OpCodes.Ret));
		IlPipelineResult result = transform(
			method,
			"game",
			registration("mod", "hook", static ctx => ctx.EmitAtStart(static e => e.Nop()))
		);
		result.DropStrongReferences();
		Assert.Throws<InternalStateException>(() => _ = result.Method);
		Assert.Throws<InternalStateException>(() => result.GetProvenance(method.Body.Instructions[0]));
	}

	private readonly record struct ManagedDelegateRequestRecord(
		MethodDefinition Method,
		Delegate Callback,
		string OwnerId,
		string LocalId
	);

	private sealed class RecordingManagedDelegateLowerer : IIlManagedDelegateLowerer {
		public List<ManagedDelegateRequestRecord> Requests { get; } = new();
		public int DisposalCount { get; private set; }

		public IlManagedDelegateLowering Lower(in IlManagedDelegateLoweringRequest request) {
			Requests.Add(new ManagedDelegateRequestRecord(request.Method, request.Callback, request.OwnerId, request.LocalId));
			MethodReference target = request.Method.Module.ImportReference(
				typeof(ManagedDelegateStub).GetMethod(nameof(ManagedDelegateStub.Invoke))
			);
			return new IlManagedDelegateLowering(
				[Instruction.Create(OpCodes.Call, target)],
				new CallbackDisposable(() => DisposalCount++)
			);
		}
	}

	private sealed class CallbackDisposable(Action callback) : IDisposable {
		private Action? callback = callback;
		public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
	}

#pragma warning disable CS0649 // field is never assigned to
	private sealed class FieldHolder {
		public static int StaticValue;
		public int InstanceValue;
	}
#pragma warning restore CS0649 // field is never assigned to

	private static class ManagedDelegateStub {
		public static void Invoke() {
		}
	}

	private static IlManipulatorRegistration registration(
		string ownerId,
		string localId,
		IlManipulator<TestL> manipulator
	) => IlManipulatorRegistration.Create(ownerId, localId, manipulator);

	private static IlPipelineResult transform(
		MethodDefinition method,
		string? baselineOwnerId,
		params IlManipulatorRegistration[] registrations
	) => IlPipelineRunner.Transform(method, baselineOwnerId, registrations, null, NullBackendOperandNormalizer.Instance);

	private static IlPipelineResult transform(
		MethodDefinition method,
		string? baselineOwnerId,
		IIlManagedDelegateLowerer managedDelegateLowerer,
		params IlManipulatorRegistration[] registrations
	) => IlPipelineRunner.Transform(method, baselineOwnerId, registrations, managedDelegateLowerer, NullBackendOperandNormalizer.Instance);

	private static MethodDefinition createVoidMethod(params Instruction[] instrs) {
		var module = ModuleDefinition.CreateModule("Test", ModuleKind.Dll);
		TypeDefinition type = new(
			"Test",
			"Container",
			Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
			module.TypeSystem.Object
		);
		module.Types.Add(type);
		MethodDefinition method = new(
			"Method",
			Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
			module.TypeSystem.Void
		);
		type.Methods.Add(method);
		foreach (Instruction instr in instrs)
			method.Body.Instructions.Add(instr);
		return method;
	}

	private static void assertCodes(MethodDefinition method, params Code[] expected) {
		Code[] actual = method.Body.Instructions.Select(static instruction => instruction.OpCode.Code).ToArray();
		Assert.Equal(expected, actual);
	}
}
