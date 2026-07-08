// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

internal enum IlInstructionSpecKind {
	// raw
	Raw,

	// loading literals
	LdcI4,

	// args
	Ldarg,
	Ldarga,
	Starg,

	// locals
	Ldloc,
	Ldloca,
	Stloc,

	// branches
	Branch,
	Switch,

	// managed delegates
	ManagedDelegate,
}

internal readonly struct IlInstructionSpec {
	public IlInstructionSpecKind Kind { get; }
	public OpCode OpCode { get; }
	public object? Operand { get; }
	public int Int { get; }
	public IlLabel[]? Labels { get; }

	private IlInstructionSpec(
		IlInstructionSpecKind kind,
		OpCode opCode = default,
		object? operand = null,
		int @int = 0,
		IlLabel[]? labels = null
	) {
		Kind = kind;
		OpCode = opCode;
		Operand = operand;
		Int = @int;
		Labels = labels;
	}

	// ======================================================================================
	// raw
	public static IlInstructionSpec Raw(OpCode opCode, object? operand = null) {
		if (opCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch || opCode.Code == Code.Switch)
			throw new ArgumentException("raw branch/switch instructions are not supported, use the label-aware emitter methods", nameof(opCode));
		return new IlInstructionSpec(IlInstructionSpecKind.Raw, opCode, operand);
	}

	// ======================================================================================
	// loading literals
	public static IlInstructionSpec LdcI4(int value) => new(IlInstructionSpecKind.LdcI4, @int: value);

	// ======================================================================================
	// args
	public static IlInstructionSpec Ldarg(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Ldarg, @int: index);
	}

	public static IlInstructionSpec Ldarga(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Ldarga, @int: index);
	}

	public static IlInstructionSpec Starg(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Starg, @int: index);
	}

	// ======================================================================================
	// locals
	public static IlInstructionSpec Ldloc(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Ldloc, @int: index);
	}

	public static IlInstructionSpec Ldloca(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Ldloca, @int: index);
	}

	public static IlInstructionSpec Stloc(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlInstructionSpec(IlInstructionSpecKind.Stloc, @int: index);
	}

	// ======================================================================================
	// branches
	public static IlInstructionSpec Branch(OpCode opCode, IlLabel target) {
		if (opCode.FlowControl is not FlowControl.Branch and not FlowControl.Cond_Branch || opCode.Code == Code.Switch)
			throw new ArgumentException("opcode is not a branch opcode", nameof(opCode));
		return new IlInstructionSpec(IlInstructionSpecKind.Branch, opCode, labels: new[] { target });
	}

	public static IlInstructionSpec Switch(ReadOnlySpan<IlLabel> targets)
		=> new(IlInstructionSpecKind.Switch, OpCodes.Switch, labels: targets.ToArray());

	// ======================================================================================
	// managed delegates
	public static IlInstructionSpec ManagedDelegate(Delegate callback) {
		ArgumentNullException.ThrowIfNull(callback);
		return new IlInstructionSpec(IlInstructionSpecKind.ManagedDelegate, operand: callback);
	}

	// ======================================================================================
	// other methods
	public IEnumerable<IlLabel> GetReferencedLabels() => Labels ?? Array.Empty<IlLabel>();
}
