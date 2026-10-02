// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Creates <see cref="IlPatternElement"/> values for IL-matching canonical instructions.
/// </summary>
/// <remarks>
/// <para>
/// IL patterns match canonical instructions (see <c>docs/mods/canonical-short-form.md</c>).
/// </para>
/// <para>
/// Every factory for an opcode that takes an operand has a parameterless overload that matches any
/// operand, e.g. <c>MatchIl.Ldloc()</c> matches any `ldloc` whereas <c>MatchIl.Ldloc(1)</c> matches
/// only `ldloc.1`. Opcodes without an operand are properties instead, e.g. <see cref="Nop"/>.
/// </para>
/// <para>
/// Matching prefixes is currently unimplemented, and will be implemented before the first stable
/// release.
/// </para>
/// </remarks>
public static class MatchIl {
	// ======================================================================================
	// wildcard

	/// <summary>
	/// Matches any single canonical instruction.
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
	/// Matches the canonical instruction with the given opcode and any operand.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="opCode"/> isn't a defined opcode, is a prefix, or is a compact encoding.
	/// </exception>
	public static IlPatternElement Raw(ILOpCode opCode) {
		IlOpCodeArguments.ThrowIfNotCanonical(opCode, nameof(opCode));
		return anyOperand(opCode);
	}

	/// <summary>
	/// Matches the canonical instruction with the given opcode and operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="operand"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="opCode"/> isn't a defined opcode, is a prefix, or is a compact encoding.
	/// </exception>
	public static IlPatternElement Raw(ILOpCode opCode, IlOperand operand) {
		ArgumentNullException.ThrowIfNull(operand);
		IlOpCodeArguments.ThrowIfNotCanonical(opCode, nameof(opCode));
		return new(IlPatternElement.PatternKind.Instruction, opCode, operand);
	}

	// ======================================================================================
	// nop and basic control flow

	/// <summary>
	/// Matches the <c>nop</c> canonical instruction.
	/// </summary>
	public static IlPatternElement Nop => Raw(ILOpCode.Nop);

	/// <summary>
	/// Matches the <c>ret</c> canonical instruction.
	/// </summary>
	public static IlPatternElement Ret => Raw(ILOpCode.Ret);

	/// <summary>
	/// Matches the <c>throw</c> canonical instruction.
	/// </summary>
	public static IlPatternElement Throw => Raw(ILOpCode.Throw);

	/// <summary>
	/// Matches the <c>rethrow</c> canonical instruction.
	/// </summary>
	public static IlPatternElement Rethrow => Raw(ILOpCode.Rethrow);

	// ======================================================================================
	// basic stack ops

	/// <summary>
	/// Matches the <c>dup</c> canonical instruction.
	/// </summary>
	public static IlPatternElement Dup => Raw(ILOpCode.Dup);

	/// <summary>
	/// Matches the <c>pop</c> canonical instruction.
	/// </summary>
	public static IlPatternElement Pop => Raw(ILOpCode.Pop);

	/// <summary>
	/// Matches the <c>ldnull</c> canonical instruction.
	/// </summary>
	public static IlPatternElement Ldnull => Raw(ILOpCode.Ldnull);

	// ======================================================================================
	// loading literal values

	/// <summary>
	/// Matches the <c>ldc.i4</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement LdcI4() => anyOperand(ILOpCode.Ldc_i4);

	/// <summary>
	/// Matches the <c>ldc.i4</c> canonical instruction with the given operand.
	/// </summary>
	public static IlPatternElement LdcI4(int value) => Raw(ILOpCode.Ldc_i4, new IlInt32Operand(value));

	/// <summary>
	/// Matches the <c>ldc.i8</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement LdcI8() => anyOperand(ILOpCode.Ldc_i8);

	/// <summary>
	/// Matches the <c>ldc.i8</c> canonical instruction with the given operand.
	/// </summary>
	public static IlPatternElement LdcI8(long value) => Raw(ILOpCode.Ldc_i8, new IlInt64Operand(value));

	/// <summary>
	/// Matches the <c>ldc.r4</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement LdcR4() => anyOperand(ILOpCode.Ldc_r4);

	/// <summary>
	/// Matches the <c>ldc.r4</c> canonical instruction with the given operand.
	/// </summary>
	public static IlPatternElement LdcR4(float value) => Raw(ILOpCode.Ldc_r4, new IlFloat32Operand(value));

	/// <summary>
	/// Matches the <c>ldc.r8</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement LdcR8() => anyOperand(ILOpCode.Ldc_r8);

	/// <summary>
	/// Matches the <c>ldc.r8</c> canonical instruction with the given operand.
	/// </summary>
	public static IlPatternElement LdcR8(double value) => Raw(ILOpCode.Ldc_r8, new IlFloat64Operand(value));

	/// <summary>
	/// Matches the <c>ldstr</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldstr() => anyOperand(ILOpCode.Ldstr);

	/// <summary>
	/// Matches the <c>ldstr</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldstr(string value) => Raw(ILOpCode.Ldstr, new IlStringOperand(value ?? throw new ArgumentNullException(nameof(value))));

	// ======================================================================================
	// args

	/// <summary>
	/// Matches the <c>ldarg</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldarg() => anyOperand(ILOpCode.Ldarg);

	/// <summary>
	/// Matches the <c>ldarg</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldarg(int index) => Raw(ILOpCode.Ldarg, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Matches the <c>ldarga</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldarga() => anyOperand(ILOpCode.Ldarga);

	/// <summary>
	/// Matches the <c>ldarga</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldarga(int index) => Raw(ILOpCode.Ldarga, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Matches the <c>starg</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Starg() => anyOperand(ILOpCode.Starg);

	/// <summary>
	/// Matches the <c>starg</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Starg(int index) => Raw(ILOpCode.Starg, new IlArgumentOperand(validateIndex(index)));

	// ======================================================================================
	// locals

	/// <summary>
	/// Matches the <c>ldloc</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldloc() => anyOperand(ILOpCode.Ldloc);

	/// <summary>
	/// Matches the <c>ldloc</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldloc(int index) => Raw(ILOpCode.Ldloc, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Matches the <c>ldloca</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldloca() => anyOperand(ILOpCode.Ldloca);

	/// <summary>
	/// Matches the <c>ldloca</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldloca(int index) => Raw(ILOpCode.Ldloca, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Matches the <c>stloc</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Stloc() => anyOperand(ILOpCode.Stloc);

	/// <summary>
	/// Matches the <c>stloc</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Stloc(int index) => Raw(ILOpCode.Stloc, new IlLocalOperand(validateIndex(index)));

	// ======================================================================================
	// fields

	/// <summary>
	/// Matches the <c>ldfld</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldfld() => anyOperand(ILOpCode.Ldfld);

	/// <summary>
	/// Matches the <c>ldfld</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldfld(IlFieldRef field) => Raw(ILOpCode.Ldfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the <c>ldflda</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldflda() => anyOperand(ILOpCode.Ldflda);

	/// <summary>
	/// Matches the <c>ldflda</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldflda(IlFieldRef field) => Raw(ILOpCode.Ldflda, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the <c>stfld</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Stfld() => anyOperand(ILOpCode.Stfld);

	/// <summary>
	/// Matches the <c>stfld</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stfld(IlFieldRef field) => Raw(ILOpCode.Stfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the <c>ldsfld</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldsfld() => anyOperand(ILOpCode.Ldsfld);

	/// <summary>
	/// Matches the <c>ldsfld</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsfld(IlFieldRef field) => Raw(ILOpCode.Ldsfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the <c>ldsflda</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldsflda() => anyOperand(ILOpCode.Ldsflda);

	/// <summary>
	/// Matches the <c>ldsflda</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsflda(IlFieldRef field) => Raw(ILOpCode.Ldsflda, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	/// <summary>
	/// Matches the <c>stsfld</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Stsfld() => anyOperand(ILOpCode.Stsfld);

	/// <summary>
	/// Matches the <c>stsfld</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stsfld(IlFieldRef field) => Raw(ILOpCode.Stsfld, new IlFieldOperand(field ?? throw new ArgumentNullException(nameof(field))));

	// ======================================================================================
	// fields (reflection overloads)

	/// <summary>
	/// Matches the <c>ldfld</c> canonical instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldfld(FieldInfo field) => Ldfld(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the <c>ldflda</c> canonical instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldflda(FieldInfo field) => Ldflda(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the <c>stfld</c> canonical instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stfld(FieldInfo field) => Stfld(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the <c>ldsfld</c> canonical instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsfld(FieldInfo field) => Ldsfld(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the <c>ldsflda</c> canonical instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsflda(FieldInfo field) => Ldsflda(IlRefFactory.Field(field));

	/// <summary>
	/// Matches the <c>stsfld</c> canonical instruction using a reflection field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stsfld(FieldInfo field) => Stsfld(IlRefFactory.Field(field));

	// ======================================================================================
	// calls

	/// <summary>
	/// Matches the <c>call</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Call() => anyOperand(ILOpCode.Call);

	/// <summary>
	/// Matches the <c>call</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Call(IlMethodRef method) => Raw(ILOpCode.Call, new IlMethodOperand(method ?? throw new ArgumentNullException(nameof(method))));

	/// <summary>
	/// Matches the <c>callvirt</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Callvirt() => anyOperand(ILOpCode.Callvirt);

	/// <summary>
	/// Matches the <c>callvirt</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Callvirt(IlMethodRef method) => Raw(ILOpCode.Callvirt, new IlMethodOperand(method ?? throw new ArgumentNullException(nameof(method))));

	/// <summary>
	/// Matches the CIL <c>calli</c> instruction with any operand.
	/// </summary>
	public static IlPatternElement Calli() => anyOperand(ILOpCode.Calli);

	// TODO: calli with an operand

	// ======================================================================================
	// calls (reflection overloads)

	/// <summary>
	/// Matches the <c>call</c> canonical instruction using a reflection method.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Call(MethodBase method) => Call(IlRefFactory.Method(method));

	/// <summary>
	/// Matches the <c>callvirt</c> canonical instruction using a reflection method.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Callvirt(MethodBase method) => Callvirt(IlRefFactory.Method(method));

	// TODO: calli

	// ======================================================================================
	// object ops

	/// <summary>
	/// Matches the <c>newobj</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Newobj() => anyOperand(ILOpCode.Newobj);

	/// <summary>
	/// Matches the <c>newobj</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="constructor"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Newobj(IlMethodRef constructor) => Raw(ILOpCode.Newobj, new IlMethodOperand(constructor ?? throw new ArgumentNullException(nameof(constructor))));

	/// <summary>
	/// Matches the <c>box</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Box() => anyOperand(ILOpCode.Box);

	/// <summary>
	/// Matches the <c>box</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Box(IlTypeRef type) => Raw(ILOpCode.Box, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the <c>unbox.any</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement UnboxAny() => anyOperand(ILOpCode.Unbox_any);

	/// <summary>
	/// Matches the <c>unbox.any</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement UnboxAny(IlTypeRef type) => Raw(ILOpCode.Unbox_any, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the <c>castclass</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Castclass() => anyOperand(ILOpCode.Castclass);

	/// <summary>
	/// Matches the <c>castclass</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Castclass(IlTypeRef type) => Raw(ILOpCode.Castclass, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the <c>isinst</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Isinst() => anyOperand(ILOpCode.Isinst);

	/// <summary>
	/// Matches the <c>isinst</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Isinst(IlTypeRef type) => Raw(ILOpCode.Isinst, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the <c>newarr</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Newarr() => anyOperand(ILOpCode.Newarr);

	/// <summary>
	/// Matches the <c>newarr</c> canonical instruction with the given operand.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Newarr(IlTypeRef type) => Raw(ILOpCode.Newarr, new IlTypeOperand(type ?? throw new ArgumentNullException(nameof(type))));

	/// <summary>
	/// Matches the <c>ldtoken</c> canonical instruction with any operand.
	/// </summary>
	public static IlPatternElement Ldtoken() => anyOperand(ILOpCode.Ldtoken);

	// TODO: ldtoken with an operand

	// ======================================================================================
	// object ops (reflection overloads)

	/// <summary>
	/// Matches the <c>newobj</c> canonical instruction using a reflection constructor.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="constructor"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Newobj(ConstructorInfo constructor) => Newobj(IlRefFactory.Method(constructor));

	/// <summary>
	/// Matches the <c>box</c> canonical instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Box(Type type) => Box(IlRefFactory.Type(type));

	/// <summary>
	/// Matches the <c>unbox.any</c> canonical instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement UnboxAny(Type type) => UnboxAny(IlRefFactory.Type(type));

	/// <summary>
	/// Matches the <c>castclass</c> canonical instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Castclass(Type type) => Castclass(IlRefFactory.Type(type));

	/// <summary>
	/// Matches the <c>isinst</c> canonical instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Isinst(Type type) => Isinst(IlRefFactory.Type(type));

	/// <summary>
	/// Matches the <c>newarr</c> canonical instruction using a reflection type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Newarr(Type type) => Newarr(IlRefFactory.Type(type));
	
	// TODO: ldtoken

	// ======================================================================================
	// branches

	/// <summary>
	/// Matches the <c>br</c> canonical instruction with any target.
	/// </summary>
	public static IlPatternElement Br() => anyOperand(ILOpCode.Br);

	/// <summary>
	/// Matches the <c>brtrue</c> canonical instruction with any target.
	/// </summary>
	public static IlPatternElement Brtrue() => anyOperand(ILOpCode.Brtrue);

	/// <summary>
	/// Matches the <c>brfalse</c> canonical instruction with any target.
	/// </summary>
	public static IlPatternElement Brfalse() => anyOperand(ILOpCode.Brfalse);

	/// <summary>
	/// Matches the <c>beq</c> canonical instruction with any target.
	/// </summary>
	public static IlPatternElement Beq() => anyOperand(ILOpCode.Beq);

	/// <summary>
	/// Matches the <c>bne.un</c> canonical instruction with any target.
	/// </summary>
	public static IlPatternElement BneUn() => anyOperand(ILOpCode.Bne_un);

	/// <summary>
	/// Matches the <c>leave</c> canonical instruction with any target.
	/// </summary>
	public static IlPatternElement Leave() => anyOperand(ILOpCode.Leave);

	/// <summary>
	/// Matches the <c>switch</c> canonical instruction with any targets.
	/// </summary>
	public static IlPatternElement Switch() => anyOperand(ILOpCode.Switch);

	// TODO: either branches/switch with operands, or decide matching them can't specify operands
	// either way, also decide how "match and fish out operands" should work because that's basically
	// necessary for branches

	// ======================================================================================
	// helper methods
	private static IlPatternElement anyOperand(ILOpCode opCode) => new(IlPatternElement.PatternKind.OpCode, opCode);

	private static int validateIndex(int index) {
		if ((uint)index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return index;
	}
}
