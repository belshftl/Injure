// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

/// <summary>
/// Creates <see cref="IlPatternElement"/> values for IL pattern matching.
/// </summary>
public static class MatchIl {
	/// <summary>
	/// Matches any single CIL instruction.
	/// </summary>
	public static IlPatternElement Any => new(IlPatternElementKind.Any);

	// ======================================================================================
	// raw match
	public static IlPatternElement OpCode(OpCode opCode) => new(IlPatternElementKind.OpCode, opCode);

	// ======================================================================================
	// nop and basic control flow
	public static IlPatternElement Nop => OpCode(OpCodes.Nop);
	public static IlPatternElement Ret => OpCode(OpCodes.Ret);
	public static IlPatternElement Throw => OpCode(OpCodes.Throw);
	public static IlPatternElement Rethrow => OpCode(OpCodes.Rethrow);

	// ======================================================================================
	// basic stack ops
	public static IlPatternElement Dup => OpCode(OpCodes.Dup);
	public static IlPatternElement Pop => OpCode(OpCodes.Pop);
	public static IlPatternElement Ldnull => OpCode(OpCodes.Ldnull);

	// ======================================================================================
	// loading literal values
	public static IlPatternElement LdcI4(int value) => new(IlPatternElementKind.LdcI4, @int: value);
	public static IlPatternElement LdcI8(long value) => new(IlPatternElementKind.LdcI8, @long: value);
	public static IlPatternElement LdcR4(float value) => new(IlPatternElementKind.LdcR4, @float: value);
	public static IlPatternElement LdcR8(double value) => new(IlPatternElementKind.LdcR8, @double: value);

	// ======================================================================================
	// args
	public static IlPatternElement Ldarg(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Ldarg, @int: index);
	}

	public static IlPatternElement Ldarga(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Ldarga, @int: index);
	}

	public static IlPatternElement Starg(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Starg, @int: index);
	}

	// ======================================================================================
	// locals
	public static IlPatternElement Ldloc(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Ldloc, @int: index);
	}

	public static IlPatternElement Ldloca(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Ldloca, @int: index);
	}

	public static IlPatternElement Stloc(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Stloc, @int: index);
	}

	// ======================================================================================
	// fields
	public static IlPatternElement Ldfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.Field, OpCodes.Ldfld, field: field);
	}

	public static IlPatternElement Ldsfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.Field, OpCodes.Ldsfld, field: field);
	}

	public static IlPatternElement Stfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.Field, OpCodes.Stfld, field: field);
	}

	public static IlPatternElement Stsfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.Field, OpCodes.Stsfld, field: field);
	}

	public static IlPatternElement Ldflda(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.Field, OpCodes.Ldflda, field: field);
	}

	public static IlPatternElement Ldsflda(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.Field, OpCodes.Ldsflda, field: field);
	}

	// ======================================================================================
	// calls
	public static IlPatternElement Call(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		return new IlPatternElement(IlPatternElementKind.Call, method: method);
	}

	public static IlPatternElement Callvirt(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		return new IlPatternElement(IlPatternElementKind.Callvirt, method: method);
	}
}
