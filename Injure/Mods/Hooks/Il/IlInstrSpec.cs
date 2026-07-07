// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil.Cil;

namespace Injure.Mods.Hooks.Il;

internal enum IlInstructionSpecKind {
	Raw,
	Ldarg,
	LdcI4,
	Ldloc,
	Stloc,
	ManagedDelegate,
	Branch,
	Switch,
}

internal readonly struct IlInstructionSpec {
	public IlInstructionSpecKind Kind { get; }
	public OpCode OpCode { get; }
	public object? Operand { get; }
	public int Integer { get; }
	public IlLabel[]? Labels { get; }

	private IlInstructionSpec(IlInstructionSpecKind kind, OpCode opCode = default, object? operand = null, int integer = 0, IlLabel[]? labels = null) {
		Kind = kind;
		OpCode = opCode;
		Operand = operand;
		Integer = integer;
		Labels = labels;
	}

	public static IlInstructionSpec Raw(OpCode opCode, object? operand = null) {
		if (opCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch || opCode.Code == Code.Switch)
			throw new ArgumentException("raw branch/switch instructions are not supported, use the label-aware emitter methods", nameof(opCode));
		return new IlInstructionSpec(IlInstructionSpecKind.Raw, opCode, operand);
	}

	public static IlInstructionSpec Ldarg(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Ldarg, integer: index);
	}

	public static IlInstructionSpec LdcI4(int value) =>
		new(IlInstructionSpecKind.LdcI4, integer: value);

	public static IlInstructionSpec Ldloc(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Ldloc, integer: index);
	}

	public static IlInstructionSpec Stloc(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Stloc, integer: index);
	}

	public static IlInstructionSpec ManagedDelegate(Delegate callback) {
		ArgumentNullException.ThrowIfNull(callback);
		return new IlInstructionSpec(IlInstructionSpecKind.ManagedDelegate, operand: callback);
	}

	public static IlInstructionSpec Branch(OpCode opCode, IlLabel target) {
		if (opCode.FlowControl is not FlowControl.Branch and not FlowControl.Cond_Branch || opCode.Code == Code.Switch)
			throw new ArgumentException("opcode is not a branch opcode", nameof(opCode));
		return new IlInstructionSpec(IlInstructionSpecKind.Branch, opCode, labels: new[] { target });
	}

	public static IlInstructionSpec Switch(ReadOnlySpan<IlLabel> targets)
		=> new(IlInstructionSpecKind.Switch, OpCodes.Switch, labels: targets.ToArray());

	public IEnumerable<IlLabel> GetReferencedLabels() => Labels ?? Array.Empty<IlLabel>();
}
