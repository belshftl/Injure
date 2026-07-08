// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

/// <summary>
/// Builder for one emitted IL fragment.
/// </summary>
/// <remarks>
/// This type records semantic operations. Labels and managed delegates are resolved only when the
/// overarching manipulation transaction commits.
/// </remarks>
public readonly ref struct IlEmitter {
	private readonly IlFragmentBuilder builder;
	internal IlEmitter(IlFragmentBuilder builder) {
		this.builder = builder ?? throw new InternalStateException("IlEmitted constructed with null builder");
	}

	// ======================================================================================
	// non-emission methods

	/// <summary>
	/// Marks the current fragment position as the target of a previously defined label.
	/// </summary>
	public void MarkLabel(IlLabel label) => builder.MarkLabel(label);

	// ======================================================================================
	// raw emit

	/// <summary>
	/// Emits the given CIL instruction with no operand.
	/// </summary>
	/// <remarks>
	/// Raw branch/switch instructions are not supported; use the label-aware emitter methods instead.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="opCode"/> is a branch/switch instruction.
	/// </exception>
	public void Raw(OpCode opCode) => builder.Emit(IlInstructionSpec.Raw(opCode));

	/// <summary>
	/// Emits the given CIL instruction with an operand.
	/// </summary>
	/// <remarks>
	/// Raw branch/switch instructions are not supported; use the label-aware emitter methods instead.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="opCode"/> is a branch/switch instruction.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="operand"/> is <see langword="null"/>.
	/// </exception>
	public void Raw(OpCode opCode, object operand) {
		ArgumentNullException.ThrowIfNull(operand);
		builder.Emit(IlInstructionSpec.Raw(opCode, operand));
	}

	// ======================================================================================
	// nop and basic control flow

	/// <summary>
	/// Emits the CIL <c>nop</c> instruction.
	/// </summary>
	public void Nop() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Nop));

	/// <summary>
	/// Emits the CIL <c>ret</c> instruction.
	/// </summary>
	public void Ret() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Ret));

	/// <summary>
	/// Emits the CIL <c>throw</c> instruction.
	/// </summary>
	public void Throw() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Throw));

	/// <summary>
	/// Emits the CIL <c>rethrow</c> instruction.
	/// </summary>
	public void Rethrow() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Rethrow));

	// ======================================================================================
	// basic stack ops

	/// <summary>
	/// Emits the CIL <c>dup</c> instruction.
	/// </summary>
	public void Dup() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Dup));

	/// <summary>
	/// Emits the CIL <c>pop</c> instruction.
	/// </summary>
	public void Pop() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Pop));

	/// <summary>
	/// Emits the CIL <c>ldnull</c> instruction.
	/// </summary>
	public void Ldnull() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldnull));

	// ======================================================================================
	// loading literal values

	/// <summary>
	/// Emits the CIL <c>ldc.i4</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>ldc.i4.m1</c> or <c>ldc.i4.s</c>.
	/// </summary>
	public void LdcI4(int value) => builder.Emit(IlInstructionSpec.LdcI4(value));

	/// <summary>
	/// Emits the CIL <c>ldc.i8</c> instruction.
	/// </summary>
	public void LdcI8(long value) => builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldc_I8, value));

	/// <summary>
	/// Emits the CIL <c>ldc.r4</c> instruction.
	/// </summary>
	public void LdcR4(float value) => builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldc_R4, value));

	/// <summary>
	/// Emits the CIL <c>ldc.r8</c> instruction.
	/// </summary>
	public void LdcR8(double value) => builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldc_R8, value));

	/// <summary>
	/// Emits the CIL <c>ldstr</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public void Ldstr(string value) {
		ArgumentNullException.ThrowIfNull(value);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldstr, value));
	}

	// ======================================================================================
	// args

	/// <summary>
	/// Emits the CIL <c>ldarg</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>ldarg.0</c> or <c>ldarg.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldarg(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		builder.Emit(IlInstructionSpec.Ldarg(index));
	}

	/// <summary>
	/// Emits the CIL <c>ldarga</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>ldarga.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldarga(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		builder.Emit(IlInstructionSpec.Ldarga(index));
	}

	/// <summary>
	/// Emits the CIL <c>starg</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>starg.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Starg(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		builder.Emit(IlInstructionSpec.Starg(index));
	}

	// ======================================================================================
	// locals

	/// <summary>
	/// Emits the CIL <c>ldloc</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>ldloc.0</c> or <c>ldloc.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldloc(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		builder.Emit(IlInstructionSpec.Ldloc(index));
	}

	/// <summary>
	/// Emits the CIL <c>ldloca</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>ldloca.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldloca(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		builder.Emit(IlInstructionSpec.Ldloca(index));
	}

	/// <summary>
	/// Emits the CIL <c>stloc</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>stloc.0</c> or <c>stloc.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Stloc(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		builder.Emit(IlInstructionSpec.Stloc(index));
	}

	// ======================================================================================
	// fields

	/// <summary>
	/// Emits the CIL <c>ldfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldfld, field));
	}

	/// <summary>
	/// Emits the CIL <c>ldsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldsfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldsfld, field));
	}

	/// <summary>
	/// Emits the CIL <c>stfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Stfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Stfld, field));
	}

	/// <summary>
	/// Emits the CIL <c>stsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Stsfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Stsfld, field));
	}

	/// <summary>
	/// Emits the CIL <c>ldflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldflda(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldflda, field));
	}

	/// <summary>
	/// Emits the CIL <c>ldsflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldsflda(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldsflda, field));
	}

	// ======================================================================================
	// calls

	/// <summary>
	/// Emits the CIL <c>call</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public void Call(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Call, method));
	}

	/// <summary>
	/// Emits the CIL <c>callvirt</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public void Callvirt(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Callvirt, method));
	}

	// ======================================================================================
	// branches

	/// <summary>
	/// Emits the CIL <c>br</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>br.s</c>.
	/// </summary>
	public void Br(IlLabel target) => builder.Emit(IlInstructionSpec.Branch(OpCodes.Br, target));

	/// <summary>
	/// Emits the CIL <c>brtrue</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>brtrue.s</c>.
	/// </summary>
	public void Brtrue(IlLabel target) => builder.Emit(IlInstructionSpec.Branch(OpCodes.Brtrue, target));

	/// <summary>
	/// Emits the CIL <c>brfalse</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>brfalse.s</c>.
	/// </summary>
	public void Brfalse(IlLabel target) => builder.Emit(IlInstructionSpec.Branch(OpCodes.Brfalse, target));

	/// <summary>
	/// Emits the CIL <c>leave</c> instruction or, if applicable, an equivalent short-form encoding
	/// such as <c>leave.s</c>.
	/// </summary>
	public void Leave(IlLabel target) => builder.Emit(IlInstructionSpec.Branch(OpCodes.Leave, target));

	/// <summary>
	/// Emits the CIL <c>switch</c> instruction.
	/// </summary>
	public void Switch(ReadOnlySpan<IlLabel> targets) => builder.Emit(IlInstructionSpec.Switch(targets));

	// ======================================================================================
	// managed delegates

	/// <summary>
	/// Emits a call to a managed delegate. The exact emitted CIL instructions are backend-dependent
	/// and should not be treated as part of the API.
	/// </summary>
	/// <typeparam name="TDelegate">Delegate type to invoke.</typeparam>
	/// <param name="callback">
	/// Delegate instance retained for as long as the transformed runtime hook remains installed.
	/// </param>
	/// <remarks>
	/// <para>
	/// It is highly recommended that <paramref name="callback"/> is a plain static method;
	/// instance methods or capturing lambdas retain state for what's typically the rest of the
	/// generation, and static lambdas commonly emit worse IL at the callsite.
	/// </para>
	/// <para>
	/// Arguments for the delegate invocation must already be on the stack. The delegate's
	/// return value, if any, remains on the stack.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="callback"/> is <see langword="null"/>.
	/// </exception>
	public void Delegate<TDelegate>(TDelegate callback) where TDelegate : Delegate {
		ArgumentNullException.ThrowIfNull(callback);
		builder.Emit(IlInstructionSpec.ManagedDelegate(callback));
	}
}
