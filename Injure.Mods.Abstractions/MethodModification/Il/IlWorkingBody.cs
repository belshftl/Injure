// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal sealed class IlWorkingBody : IStrongRefDroppable {
	private Dictionary<Instruction, InternalIlProvenance> provenance;

	public MethodDefinition Method { get; }
	public MethodBody Body { get; }

	private IlWorkingBody(MethodDefinition method, MethodBody body, Dictionary<Instruction, InternalIlProvenance> provenance) {
		Method = method ?? throw new InternalStateException("IlWorkingBody constructed with null method definition");
		Body = body ?? throw new InternalStateException("IlWorkingBody constructed with null method body");
		this.provenance = provenance ?? throw new InternalStateException("IlWorkingBody constructed with null provenance dictionary");
	}

	public static IlWorkingBody Clone(MethodDefinition method, InternalIlProvenance baseline, IIlBackendOperandNormalizer operandNormalizer) {
		MethodBody source = method.Body;
		MethodBody body = new(method) {
			InitLocals = source.InitLocals,
			MaxStackSize = source.MaxStackSize,
		};

		Dictionary<VariableDefinition, VariableDefinition> vars = new();
		foreach (VariableDefinition sourceVar in source.Variables) {
			VariableDefinition @var = new(sourceVar.VariableType);
			body.Variables.Add(@var);
			vars.Add(sourceVar, @var);
		}

		Dictionary<Instruction, Instruction> instrs = new(InstructionReferenceComparer.Instance);
		foreach (Instruction sourceInstrs in source.Instructions) {
			Instruction instr = createInstrShell(sourceInstrs, operandNormalizer);
			body.Instructions.Add(instr);
			instrs.Add(sourceInstrs, instr);
		}

		for (int i = 0; i < source.Instructions.Count; i++) {
			Instruction sourceInstr = source.Instructions[i];
			Instruction instr = body.Instructions[i];
			instr.Operand = cloneOperand(sourceInstr.Operand, source, body, instrs, vars, operandNormalizer);
		}

		foreach (ExceptionHandler sourceHandler in source.ExceptionHandlers) {
			ExceptionHandler handler = new(sourceHandler.HandlerType) {
				CatchType = sourceHandler.CatchType,
				TryStart = mapInstr(sourceHandler.TryStart, instrs),
				TryEnd = mapInstr(sourceHandler.TryEnd, instrs),
				HandlerStart = mapInstr(sourceHandler.HandlerStart, instrs),
				HandlerEnd = mapInstr(sourceHandler.HandlerEnd, instrs),
				FilterStart = mapInstr(sourceHandler.FilterStart, instrs),
			};
			body.ExceptionHandlers.Add(handler);
		}

		Dictionary<Instruction, InternalIlProvenance> provenance = new(InstructionReferenceComparer.Instance);
		foreach (Instruction instr in body.Instructions)
			provenance.Add(instr, baseline);

		return new IlWorkingBody(method, body, provenance);
	}

	public IlSnapshot CaptureSnapshot() {
		Instruction[] instrs = Body.Instructions.ToArray();
		var provValues = new InternalIlProvenance[instrs.Length];
		for (int i = 0; i < instrs.Length; i++)
			provValues[i] = provenance[instrs[i]];
		return new IlSnapshot(Method, instrs, provValues);
	}

	public InternalIlProvenance GetProvenance(Instruction instr) => provenance[instr];

	public void ReplaceInstructions(IReadOnlyList<Instruction> instrs, Dictionary<Instruction, InternalIlProvenance> newProvenance) {
		Body.Instructions.Clear();
		foreach (Instruction instr in instrs)
			Body.Instructions.Add(instr);
		provenance = newProvenance;
	}

	public Dictionary<Instruction, InternalIlProvenance> CopyProvenance() => new(provenance, InstructionReferenceComparer.Instance);

	public void DropStrongReferences() {
		Body.Instructions.Clear();
		Body.ExceptionHandlers.Clear();
		Body.Variables.Clear();
		provenance.Clear();
	}

	private static Instruction createInstrShell(Instruction source, IIlBackendOperandNormalizer operandNormalizer) {
		OpCode opCode = source.OpCode;
		object? operand = source.Operand;
		object? normalized = operandNormalizer.NormalizeOperand(operand);
		return normalized switch {
			null => Instruction.Create(opCode),
			Instruction => Instruction.Create(opCode, Instruction.Create(OpCodes.Nop)),
			Instruction[] => Instruction.Create(opCode, Array.Empty<Instruction>()),
			sbyte val => Instruction.Create(opCode, val),
			byte val => Instruction.Create(opCode, val),
			int val => Instruction.Create(opCode, val),
			long val => Instruction.Create(opCode, val),
			float val => Instruction.Create(opCode, val),
			double val => Instruction.Create(opCode, val),
			string s => Instruction.Create(opCode, s),
			TypeReference t => Instruction.Create(opCode, t),
			FieldReference f => Instruction.Create(opCode, f),
			MethodReference m => Instruction.Create(opCode, m),
			CallSite cs => Instruction.Create(opCode, cs),
			VariableDefinition v => Instruction.Create(opCode, v),
			ParameterDefinition p => Instruction.Create(opCode, p),
			_ => throw new IlPipelineException($"unsupported Cecil operand type '{normalized.GetType()}' (normalized by backend from '{operand.GetType()}') while cloning"),
		};
	}

	private static object? cloneOperand(
		object? operand,
		MethodBody sourceBody,
		MethodBody targetBody,
		Dictionary<Instruction, Instruction> instrs,
		Dictionary<VariableDefinition, VariableDefinition> vars,
		IIlBackendOperandNormalizer operandNormalizer
	) {
		object? normalized = operandNormalizer.NormalizeOperand(operand);
		return normalized switch {
			Instruction instr => instrs[instr],
			Instruction[] targets => targets.Select(target => instrs[target]).ToArray(),
			VariableDefinition @var => vars[@var],
			ParameterDefinition param when ReferenceEquals(param, sourceBody.ThisParameter) => targetBody.ThisParameter,
			_ => normalized,
		};
	}

	private static Instruction? mapInstr(Instruction? instr, Dictionary<Instruction, Instruction> instrs) =>
		instr is null ? null : instrs[instr];
}
