// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Hooks.Il;

/// <summary>
/// Creates <see cref="IlPatternElement"/> values for IL pattern matching.
/// </summary>
public static class MatchIl {
	public static IlPatternElement Any => new(IlPatternElementKind.Any);
	public static IlPatternElement Dup => OpCode(OpCodes.Dup);
	public static IlPatternElement Nop => OpCode(OpCodes.Nop);
	public static IlPatternElement Pop => OpCode(OpCodes.Pop);
	public static IlPatternElement Ret => OpCode(OpCodes.Ret);

	public static IlPatternElement OpCode(OpCode opCode) =>
		new(IlPatternElementKind.OpCode, opCode);

	public static IlPatternElement Call(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		return new IlPatternElement(IlPatternElementKind.Call, method: method);
	}

	public static IlPatternElement Callvirt(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		return new IlPatternElement(IlPatternElementKind.Callvirt, method: method);
	}

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

	public static IlPatternElement Ldarg(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Ldarg, integer: index);
	}

	public static IlPatternElement LdcI4(int value) =>
		new(IlPatternElementKind.LdcI4, integer: value);

	public static IlPatternElement Ldloc(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Ldloc, integer: index);
	}

	public static IlPatternElement Stloc(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlPatternElement(IlPatternElementKind.Stloc, integer: index);
	}
}
