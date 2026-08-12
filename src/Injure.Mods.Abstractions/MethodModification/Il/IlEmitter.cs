// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Builder for one emitted IL fragment.
/// </summary>
/// <remarks>
/// <para>
/// Emitted instructions are recorded semantically, not encoded. Compact encoding forms are not
/// preserved: an instruction is stored in its canonical form, so a value that could be written as a
/// short form is indistinguishable afterwards from one that could not, both to later manipulators
/// and to the encoder, which independently chooses the shortest legal encoding.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly ref struct IlEmitter {
	private IlFragmentBuilder builder => field ?? throw new InvalidOperationException("this IlEmitter value is uninitialized/invalid");

	internal IlEmitter(IlFragmentBuilder builder) {
		InternalStateException.ThrowIfNull(builder);
		this.builder = builder;
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
	/// <para>
	/// The opcode is canonicalized: passing a compact form such as <c>ldc.i4.s</c> emits, and is
	/// subsequently indistinguishable from, the corresponding long form. Prefix opcodes are rejected;
	/// a prefix is part of the instruction it applies to rather than an instruction of its own.
	/// </para>
	/// <para>
	/// Raw branch/switch instructions are not supported; use <see cref="Branch(ILOpCode, IlLabel)"/>.
	/// </para>
	/// </remarks>
	public void Raw(ILOpCode opCode) => builder.Emit(IlInstructionSpec.Raw(opCode));

	/// <summary>
	/// Emits the given CIL instruction with an operand.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The opcode is canonicalized: passing a compact form such as <c>ldc.i4.s</c> emits, and is
	/// subsequently indistinguishable from, the corresponding long form. Prefix opcodes are rejected;
	/// a prefix is part of the instruction it applies to rather than an instruction of its own.
	/// </para>
	/// <para>
	/// Raw branch/switch instructions are not supported; use <see cref="Branch(ILOpCode, IlLabel)"/>.
	/// </para>
	/// </remarks>
	public void Raw(ILOpCode opCode, IlOperand operand) {
		ArgumentNullException.ThrowIfNull(operand);
		builder.Emit(IlInstructionSpec.Raw(opCode, operand));
	}

	// ======================================================================================
	// nop and basic control flow

	/// <summary>
	/// Emits the CIL <c>nop</c> instruction.
	/// </summary>
	public void Nop() => Raw(ILOpCode.Nop);

	/// <summary>
	/// Emits the CIL <c>ret</c> instruction.
	/// </summary>
	public void Ret() => Raw(ILOpCode.Ret);

	/// <summary>
	/// Emits the CIL <c>throw</c> instruction.
	/// </summary>
	public void Throw() => Raw(ILOpCode.Throw);

	/// <summary>
	/// Emits the CIL <c>rethrow</c> instruction.
	/// </summary>
	public void Rethrow() => Raw(ILOpCode.Rethrow);

	// ======================================================================================
	// basic stack ops

	/// <summary>
	/// Emits the CIL <c>dup</c> instruction.
	/// </summary>
	public void Dup() => Raw(ILOpCode.Dup);

	/// <summary>
	/// Emits the CIL <c>pop</c> instruction.
	/// </summary>
	public void Pop() => Raw(ILOpCode.Pop);

	/// <summary>
	/// Emits the CIL <c>ldnull</c> instruction.
	/// </summary>
	public void Ldnull() => Raw(ILOpCode.Ldnull);

	// ======================================================================================
	// loading literal values

	/// <summary>
	/// Emits the CIL <c>ldc.i4</c> instruction. The encoder may select an equivalent short form.
	/// </summary>
	public void LdcI4(int value) => Raw(ILOpCode.Ldc_i4, new IlInt32Operand(value));

	/// <summary>
	/// Emits the CIL <c>ldc.i8</c> instruction.
	/// </summary>
	public void LdcI8(long value) => Raw(ILOpCode.Ldc_i8, new IlInt64Operand(value));

	/// <summary>
	/// Emits the CIL <c>ldc.r4</c> instruction.
	/// </summary>
	public void LdcR4(float value) => Raw(ILOpCode.Ldc_r4, new IlFloat32Operand(value));

	/// <summary>
	/// Emits the CIL <c>ldc.r8</c> instruction.
	/// </summary>
	public void LdcR8(double value) => Raw(ILOpCode.Ldc_r8, new IlFloat64Operand(value));

	/// <summary>
	/// Emits the CIL <c>ldstr</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public void Ldstr(string value) => Raw(ILOpCode.Ldstr, new IlStringOperand(value ?? throw new ArgumentNullException(nameof(value))));

	// ======================================================================================
	// args

	/// <summary>
	/// Emits the CIL <c>ldarg</c> instruction. The encoder may select an equivalent short form.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldarg(int index) => Raw(ILOpCode.Ldarg, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Emits the CIL <c>ldarga</c> instruction. The encoder may select <c>ldarga.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldarga(int index) => Raw(ILOpCode.Ldarga, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Emits the CIL <c>starg</c> instruction. The encoder may select <c>starg.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Starg(int index) => Raw(ILOpCode.Starg, new IlArgumentOperand(validateIndex(index)));

	// ======================================================================================
	// locals

	/// <summary>
	/// Emits the CIL <c>ldloc</c> instruction. The encoder may select an equivalent short form.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldloc(int index) => Raw(ILOpCode.Ldloc, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Emits the CIL <c>ldloca</c> instruction. The encoder may select <c>ldloca.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldloca(int index) => Raw(ILOpCode.Ldloca, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Emits the CIL <c>stloc</c> instruction. The encoder may select an equivalent short form.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Stloc(int index) => Raw(ILOpCode.Stloc, new IlLocalOperand(validateIndex(index)));

	// ======================================================================================
	// fields

	/// <summary>
	/// Emits the CIL <c>ldfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldfld(IlFieldRef field) => Raw(ILOpCode.Ldfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Emits the CIL <c>ldflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldflda(IlFieldRef field) => Raw(ILOpCode.Ldflda, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Emits the CIL <c>stfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Stfld(IlFieldRef field) => Raw(ILOpCode.Stfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Emits the CIL <c>ldsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldsfld(IlFieldRef field) => Raw(ILOpCode.Ldsfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Emits the CIL <c>ldsflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldsflda(IlFieldRef field) => Raw(ILOpCode.Ldsflda, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Emits the CIL <c>stsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Stsfld(IlFieldRef field) => Raw(ILOpCode.Stsfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	// ======================================================================================
	// calls

	/// <summary>
	/// Emits the CIL <c>call</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public void Call(IlMethodRef method) => Raw(ILOpCode.Call, new IlMethodOperand(method ?? throw new ArgumentNullException(nameof(method))));

	/// <summary>
	/// Emits the CIL <c>callvirt</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public void Callvirt(IlMethodRef method) => Raw(ILOpCode.Callvirt, new IlMethodOperand(method ?? throw new ArgumentNullException(nameof(method))));

	/// <summary>
	/// Emits the CIL <c>calli</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="signature"/> is <see langword="null"/>.
	/// </exception>
	public void Calli(IlMethodSignature signature) => Raw(ILOpCode.Calli, new IlCallSiteOperand(signature ?? throw new ArgumentNullException(nameof(signature))));

	// ======================================================================================
	// object ops

	/// <summary>
	/// Emits the CIL <c>newobj</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="constructor"/> is <see langword="null"/>.
	/// </exception>
	public void Newobj(IlMethodRef constructor) => Raw(ILOpCode.Newobj, new IlMethodOperand(constructor ?? throw new ArgumentNullException(nameof(constructor))));

	/// <summary>
	/// Emits the CIL <c>box</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public void Box(IlTypeRef type) => Raw(ILOpCode.Box, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Emits the CIL <c>unbox.any</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public void UnboxAny(IlTypeRef type) => Raw(ILOpCode.Unbox_any, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Emits the CIL <c>castclass</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public void Castclass(IlTypeRef type) => Raw(ILOpCode.Castclass, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Emits the CIL <c>isinst</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public void Isinst(IlTypeRef type) => Raw(ILOpCode.Isinst, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Emits the CIL <c>newarr</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public void Newarr(IlTypeRef type) => Raw(ILOpCode.Newarr, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Emits the CIL <c>ldtoken</c> instruction for a type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public void Ldtoken(IlTypeRef type) => Raw(ILOpCode.Ldtoken, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Emits the CIL <c>ldtoken</c> instruction for a method.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public void Ldtoken(IlMethodRef method) => Raw(ILOpCode.Ldtoken, new IlMethodOperand(method ?? throw new ArgumentNullException(nameof(method))));

	/// <summary>
	/// Emits the CIL <c>ldtoken</c> instruction for a field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public void Ldtoken(IlFieldRef field) => Raw(ILOpCode.Ldtoken, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	// ======================================================================================
	// branches

	/// <summary>
	/// Emits a branch opcode targeting a transaction-scoped label.
	/// </summary>
	public void Branch(ILOpCode opCode, IlLabel target) => builder.Emit(IlInstructionSpec.Branch(opCode, target));

	/// <summary>
	/// Emits the CIL <c>br</c> instruction.
	/// </summary>
	public void Br(IlLabel target) => Branch(ILOpCode.Br, target);

	/// <summary>
	/// Emits the CIL <c>brtrue</c> instruction.
	/// </summary>
	public void Brtrue(IlLabel target) => Branch(ILOpCode.Brtrue, target);

	/// <summary>
	/// Emits the CIL <c>brfalse</c> instruction.
	/// </summary>
	public void Brfalse(IlLabel target) => Branch(ILOpCode.Brfalse, target);

	/// <summary>
	/// Emits the CIL <c>beq</c> instruction.
	/// </summary>
	public void Beq(IlLabel target) => Branch(ILOpCode.Beq, target);

	/// <summary>
	/// Emits the CIL <c>bne.un</c> instruction.
	/// </summary>
	public void BneUn(IlLabel target) => Branch(ILOpCode.Bne_un, target);

	/// <summary>
	/// Emits the CIL <c>leave</c> instruction.
	/// </summary>
	public void Leave(IlLabel target) => Branch(ILOpCode.Leave, target);

	/// <summary>
	/// Emits the CIL <c>switch</c> instruction targeting the supplied labels.
	/// </summary>
	public void Switch(ReadOnlySpan<IlLabel> targets) => builder.Emit(IlInstructionSpec.Switch(targets));

	// ======================================================================================
	// helper methods
	private static int validateIndex(int index) {
		if ((uint)index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return index;
	}
}
