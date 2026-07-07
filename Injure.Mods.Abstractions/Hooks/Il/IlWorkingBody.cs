// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

internal sealed class IlWorkingBody : IStrongRefDroppable {
	private Dictionary<Instruction, InternalIlProvenance> provenance;

	public MethodDefinition Method { get; }
	public MethodBody Body { get; }

	private IlWorkingBody(MethodDefinition method, MethodBody body, Dictionary<Instruction, InternalIlProvenance> provenance) {
		Method = method ?? throw new InternalStateException("IlWorkingBody constructed with null method definition");
		Body = body ?? throw new InternalStateException("IlWorkingBody constructed with null method body");
		this.provenance = provenance ?? throw new InternalStateException("IlWorkingBody constructed with null provenance dictionary");
	}

	public static IlWorkingBody Clone(MethodDefinition method, InternalIlProvenance baseline) {
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
			Instruction instr = createInstrShell(sourceInstrs);
			body.Instructions.Add(instr);
			instrs.Add(sourceInstrs, instr);
		}

		for (int i = 0; i < source.Instructions.Count; i++) {
			Instruction sourceInstr = source.Instructions[i];
			Instruction instr = body.Instructions[i];
			instr.Operand = cloneOperand(sourceInstr.Operand, source, body, instrs, vars);
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

	private static Instruction createInstrShell(Instruction source) {
		OpCode opCode = source.OpCode;
		object? operand = source.Operand;
		return operand switch {
			null => Instruction.Create(opCode),
			Instruction => Instruction.Create(opCode, Instruction.Create(OpCodes.Nop)),
			Instruction[] => Instruction.Create(opCode, Array.Empty<Instruction>()),
			sbyte value => Instruction.Create(opCode, value),
			byte value => Instruction.Create(opCode, value),
			int value => Instruction.Create(opCode, value),
			long value => Instruction.Create(opCode, value),
			float value => Instruction.Create(opCode, value),
			double value => Instruction.Create(opCode, value),
			string value => Instruction.Create(opCode, value),
			TypeReference value => Instruction.Create(opCode, value),
			FieldReference value => Instruction.Create(opCode, value),
			MethodReference value => Instruction.Create(opCode, value),
			CallSite value => Instruction.Create(opCode, value),
			VariableDefinition value => Instruction.Create(opCode, value),
			ParameterDefinition value => Instruction.Create(opCode, value),
			_ => throw new IlPipelineException($"unsupported Cecil operand type '{operand.GetType()}' while cloning"),
		};
	}

	private static object? cloneOperand(
		object? operand,
		MethodBody sourceBody,
		MethodBody targetBody,
		Dictionary<Instruction, Instruction> instrs,
		Dictionary<VariableDefinition, VariableDefinition> vars
	) {
		return operand switch {
			null => null,
			Instruction instr => instrs[instr],
			Instruction[] targets => targets.Select(target => instrs[target]).ToArray(),
			VariableDefinition @var => vars[@var],
			ParameterDefinition param when ReferenceEquals(param, sourceBody.ThisParameter) => targetBody.ThisParameter,
			_ => operand,
		};
	}

	private static Instruction? mapInstr(Instruction? instr, Dictionary<Instruction, Instruction> instrs) =>
		instr is null ? null : instrs[instr];
}
