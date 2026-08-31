// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Creates <see cref="IlPatternElement"/> values for IL pattern matching.
/// </summary>
/// <remarks>
/// <para>
/// Patterns match canonical instructions, not encodings. An element built for a long form also
/// matches every short/compact encoding of the same instruction: <c>MatchIl.LdcI4(0)</c> matches
/// <c>ldc.i4.0</c>, and <c>MatchIl.Ldarg(1)</c> matches <c>ldarg.1</c>, etc. There is no way to match
/// one encoding but not another, and no reason to want one, since the encoder re-chooses the encoding
/// independently of what the body was decoded from.
/// </para>
/// <para>
/// Prefixes are currently not matchable.
/// </para>
/// </remarks>
public static class MatchIl {
	// ======================================================================================
	// wildcard

	/// <summary>
	/// Matches any single CIL instruction.
	/// </summary>
	/// <remarks>
	/// Matches exactly one instruction, not a run of them. Patterns have no repetition or wildcard
	/// operators, at least not yet; a variable-length gap currently must be handled by matching the
	/// two ends separately.
	/// </remarks>
	public static IlPatternElement Any => new(IlPatternElement.PatternKind.Any);

	// ======================================================================================
	// raw match

	/// <summary>
	/// Matches the CIL instruction with the given opcode.
	/// </summary>
	/// <remarks>
	/// Matches only by opcode, accepting any operand. The opcode is canonicalized, so passing a compact
	/// form matches the same instructions as passing its long form.
	/// </remarks>
	public static IlPatternElement OpCode(ILOpCode opCode) => new(IlPatternElement.PatternKind.OpCode, opCode);

	/// <summary>
	/// Matches the CIL instruction with the given opcode and operand.
	/// </summary>
	public static IlPatternElement Instruction(ILOpCode opCode, IlOperand operand) =>
		new(IlPatternElement.PatternKind.Instruction, opCode, operand ?? throw new ArgumentNullException(nameof(operand)));

	// ======================================================================================
	// nop and basic control flow

	/// <summary>
	/// Matches the CIL <c>nop</c> instruction.
	/// </summary>
	public static IlPatternElement Nop => OpCode(ILOpCode.Nop);

	/// <summary>
	/// Matches the CIL <c>ret</c> instruction.
	/// </summary>
	public static IlPatternElement Ret => OpCode(ILOpCode.Ret);

	/// <summary>
	/// Matches the CIL <c>throw</c> instruction.
	/// </summary>
	public static IlPatternElement Throw => OpCode(ILOpCode.Throw);

	/// <summary>
	/// Matches the CIL <c>rethrow</c> instruction.
	/// </summary>
	public static IlPatternElement Rethrow => OpCode(ILOpCode.Rethrow);

	// ======================================================================================
	// basic stack ops

	/// <summary>
	/// Matches the CIL <c>dup</c> instruction.
	/// </summary>
	public static IlPatternElement Dup => OpCode(ILOpCode.Dup);

	/// <summary>
	/// Matches the CIL <c>pop</c> instruction.
	/// </summary>
	public static IlPatternElement Pop => OpCode(ILOpCode.Pop);

	/// <summary>
	/// Matches the CIL <c>ldnull</c> instruction.
	/// </summary>
	public static IlPatternElement Ldnull => OpCode(ILOpCode.Ldnull);

	// ======================================================================================
	// loading literal values

	/// <summary>
	/// Matches the CIL <c>ldc.i4</c> instruction and equivalent short-form encodings such as
	/// <c>ldc.i4.m1</c> or <c>ldc.i4.s</c>.
	/// </summary>
	public static IlPatternElement LdcI4(int value) => Instruction(ILOpCode.Ldc_i4, new IlInt32Operand(value));

	/// <summary>
	/// Matches the CIL <c>ldc.i8</c> instruction.
	/// </summary>
	public static IlPatternElement LdcI8(long value) => Instruction(ILOpCode.Ldc_i8, new IlInt64Operand(value));

	/// <summary>
	/// Matches the CIL <c>ldc.r4</c> instruction.
	/// </summary>
	public static IlPatternElement LdcR4(float value) => Instruction(ILOpCode.Ldc_r4, new IlFloat32Operand(value));

	/// <summary>
	/// Matches the CIL <c>ldc.r8</c> instruction.
	/// </summary>
	public static IlPatternElement LdcR8(double value) => Instruction(ILOpCode.Ldc_r8, new IlFloat64Operand(value));

	/// <summary>
	/// Matches the CIL <c>ldstr</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldstr(string value) => Instruction(ILOpCode.Ldstr, new IlStringOperand(value ?? throw new ArgumentNullException(nameof(value))));

	// ======================================================================================
	// args

	/// <summary>
	/// Matches the CIL <c>ldarg</c> instruction and equivalent short-form encodings such as
	/// <c>ldarg.0</c> or <c>ldarg.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldarg(int index) => Instruction(ILOpCode.Ldarg, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Matches the CIL <c>ldarga</c> instruction and the equivalent <c>ldarga.s</c> encoding.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldarga(int index) => Instruction(ILOpCode.Ldarga, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Matches the CIL <c>starg</c> instruction and the equivalent <c>starg.s</c> encoding.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Starg(int index) => Instruction(ILOpCode.Starg, new IlArgumentOperand(validateIndex(index)));

	// ======================================================================================
	// locals

	/// <summary>
	/// Matches the CIL <c>ldloc</c> instruction and equivalent short-form encodings such as
	/// <c>ldloc.0</c> or <c>ldloc.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldloc(int index) => Instruction(ILOpCode.Ldloc, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Matches the CIL <c>ldloca</c> instruction and the equivalent <c>ldloca.s</c> encoding.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldloca(int index) => Instruction(ILOpCode.Ldloca, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Matches the CIL <c>stloc</c> instruction and equivalent short-form encodings such as
	/// <c>stloc.0</c> or <c>stloc.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Stloc(int index) => Instruction(ILOpCode.Stloc, new IlLocalOperand(validateIndex(index)));

	// ======================================================================================
	// fields

	/// <summary>
	/// Matches the CIL <c>ldfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldfld(IlFieldRef field) => Instruction(ILOpCode.Ldfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the CIL <c>ldflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldflda(IlFieldRef field) => Instruction(ILOpCode.Ldflda, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the CIL <c>stfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stfld(IlFieldRef field) => Instruction(ILOpCode.Stfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the CIL <c>ldsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsfld(IlFieldRef field) => Instruction(ILOpCode.Ldsfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the CIL <c>ldsflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsflda(IlFieldRef field) => Instruction(ILOpCode.Ldsflda, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the CIL <c>stsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stsfld(IlFieldRef field) => Instruction(ILOpCode.Stsfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	// ======================================================================================
	// fields (reflection overloads)

	/// <summary>
	/// Matches the CIL <c>ldfld</c> instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldfld(FieldInfo field) => Ldfld(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the CIL <c>ldflda</c> instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldflda(FieldInfo field) => Ldflda(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the CIL <c>stfld</c> instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stfld(FieldInfo field) => Stfld(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the CIL <c>ldsfld</c> instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsfld(FieldInfo field) => Ldsfld(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the CIL <c>ldsflda</c> instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsflda(FieldInfo field) => Ldsflda(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the CIL <c>stsfld</c> instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stsfld(FieldInfo field) => Stsfld(IlRefFactory.Field(field));

	// ======================================================================================
	// calls

	/// <summary>
	/// Matches the CIL <c>call</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Call(IlMethodRef method) => Instruction(ILOpCode.Call, new IlMethodOperand(method ?? throw new ArgumentNullException(nameof(method))));

	/// <summary>
	/// Matches the CIL <c>callvirt</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Callvirt(IlMethodRef method) => Instruction(ILOpCode.Callvirt, new IlMethodOperand(method ?? throw new ArgumentNullException(nameof(method))));

	// TODO: calli

	// ======================================================================================
	// calls (reflection overloads)

	/// <summary>
	/// Matches the CIL <c>call</c> instruction using a reflection method.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Call(MethodBase method) => Call(IlRefFactory.Method(method));

	/// <summary>
	/// Matches the CIL <c>callvirt</c> instruction using a reflection method.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Callvirt(MethodBase method) => Callvirt(IlRefFactory.Method(method));

	// TODO: calli

	// ======================================================================================
	// object ops

	/// <summary>
	/// Matches the CIL <c>newobj</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="constructor"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Newobj(IlMethodRef constructor) => Instruction(ILOpCode.Newobj, new IlMethodOperand(constructor ?? throw new ArgumentNullException(nameof(constructor))));

	/// <summary>
	/// Matches the CIL <c>box</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Box(IlTypeRef type) => Instruction(ILOpCode.Box, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the CIL <c>unbox.any</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement UnboxAny(IlTypeRef type) => Instruction(ILOpCode.Unbox_any, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the CIL <c>castclass</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Castclass(IlTypeRef type) => Instruction(ILOpCode.Castclass, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the CIL <c>isinst</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Isinst(IlTypeRef type) => Instruction(ILOpCode.Isinst, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the CIL <c>newarr</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Newarr(IlTypeRef type) => Instruction(ILOpCode.Newarr, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	// TODO: ldtoken

	// ======================================================================================
	// object ops (reflection overloads)

	/// <summary>
	/// Matches the CIL <c>newobj</c> instruction using a reflection constructor.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="constructor"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Newobj(ConstructorInfo constructor) => Newobj(IlRefFactory.Method(constructor));

	/// <summary>
	/// Matches the CIL <c>box</c> instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Box(Type type) => Box(IlRefFactory.Type(type));

	/// <summary>
	/// Matches the CIL <c>unbox.any</c> instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement UnboxAny(Type type) => UnboxAny(IlRefFactory.Type(type));

	/// <summary>
	/// Matches the CIL <c>castclass</c> instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Castclass(Type type) => Castclass(IlRefFactory.Type(type));

	/// <summary>
	/// Matches the CIL <c>isinst</c> instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Isinst(Type type) => Isinst(IlRefFactory.Type(type));

	/// <summary>
	/// Matches the CIL <c>newarr</c> instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Newarr(Type type) => Newarr(IlRefFactory.Type(type));
	
	// TODO: ldtoken

	// ======================================================================================
	// helper methods
	private static int validateIndex(int index) {
		if ((uint)index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return index;
	}
}
