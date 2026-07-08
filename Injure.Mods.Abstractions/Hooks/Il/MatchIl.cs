// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
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

	/// <summary>
	/// Matches the CIL instruction with the given opcode.
	/// </summary>
	public static IlPatternElement OpCode(OpCode opCode) => new(IlPatternElementKind.OpCode, opCode);

	// ======================================================================================
	// nop and basic control flow

	/// <summary>
	/// Matches the CIL <c>nop</c> instruction.
	/// </summary>
	public static IlPatternElement Nop => OpCode(OpCodes.Nop);

	/// <summary>
	/// Matches the CIL <c>ret</c> instruction.
	/// </summary>
	public static IlPatternElement Ret => OpCode(OpCodes.Ret);

	/// <summary>
	/// Matches the CIL <c>throw</c> instruction.
	/// </summary>
	public static IlPatternElement Throw => OpCode(OpCodes.Throw);

	/// <summary>
	/// Matches the CIL <c>rethrow</c> instruction.
	/// </summary>
	public static IlPatternElement Rethrow => OpCode(OpCodes.Rethrow);

	// ======================================================================================
	// basic stack ops

	/// <summary>
	/// Matches the CIL <c>dup</c> instruction.
	/// </summary>
	public static IlPatternElement Dup => OpCode(OpCodes.Dup);

	/// <summary>
	/// Matches the CIL <c>pop</c> instruction.
	/// </summary>
	public static IlPatternElement Pop => OpCode(OpCodes.Pop);

	/// <summary>
	/// Matches the CIL <c>ldnull</c> instruction.
	/// </summary>
	public static IlPatternElement Ldnull => OpCode(OpCodes.Ldnull);

	// ======================================================================================
	// loading literal values

	/// <summary>
	/// Matches the CIL <c>ldc.i4</c> instruction and, if applicable, equivalent short-form encodings
	/// such as <c>ldc.i4.m1</c> or <c>ldc.i4.s</c>.
	/// </summary>
	public static IlPatternElement LdcI4(int value) => new(IlPatternElementKind.LdcI4, @int: value);

	/// <summary>
	/// Matches the CIL <c>ldc.i8</c> instruction.
	/// </summary>
	public static IlPatternElement LdcI8(long value) => new(IlPatternElementKind.LdcI8, @long: value);

	/// <summary>
	/// Matches the CIL <c>ldc.r4</c> instruction.
	/// </summary>
	public static IlPatternElement LdcR4(float value) => new(IlPatternElementKind.LdcR4, @float: value);

	/// <summary>
	/// Matches the CIL <c>ldc.r8</c> instruction.
	/// </summary>
	public static IlPatternElement LdcR8(double value) => new(IlPatternElementKind.LdcR8, @double: value);

	/// <summary>
	/// Matches the CIL <c>ldstr</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldstr(string value) {
		ArgumentNullException.ThrowIfNull(value);
		return new IlPatternElement(IlPatternElementKind.Ldstr, @string: value);
	}

	// ======================================================================================
	// args

	/// <summary>
	/// Matches the CIL <c>ldarg</c> instruction and, if applicable, equivalent short-form encodings
	/// such as <c>ldarg.0</c> or <c>ldarg.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldarg(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return new IlPatternElement(IlPatternElementKind.Ldarg, @int: index);
	}

	/// <summary>
	/// Matches the CIL <c>ldarga</c> instruction and, if applicable, equivalent short-form encodings
	/// such as <c>ldarga.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldarga(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return new IlPatternElement(IlPatternElementKind.Ldarga, @int: index);
	}

	/// <summary>
	/// Matches the CIL <c>starg</c> instruction and, if applicable, equivalent short-form encodings
	/// such as <c>starg.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Starg(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return new IlPatternElement(IlPatternElementKind.Starg, @int: index);
	}

	// ======================================================================================
	// locals

	/// <summary>
	/// Matches the CIL <c>ldloc</c> instruction and, if applicable, equivalent short-form encodings
	/// such as <c>ldloc.0</c> or <c>ldloc.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldloc(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return new IlPatternElement(IlPatternElementKind.Ldloc, @int: index);
	}

	/// <summary>
	/// Matches the CIL <c>ldloca</c> instruction and, if applicable, equivalent short-form encodings
	/// such as <c>ldloca.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Ldloca(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return new IlPatternElement(IlPatternElementKind.Ldloca, @int: index);
	}

	/// <summary>
	/// Matches the CIL <c>stloc</c> instruction and, if applicable, equivalent short-form encodings
	/// such as <c>stloc.0</c> or <c>stloc.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public static IlPatternElement Stloc(int index) {
		if (index < 0 || index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return new IlPatternElement(IlPatternElementKind.Stloc, @int: index);
	}

	// ======================================================================================
	// fields

	/// <summary>
	/// Matches the CIL <c>ldfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.CecilField, OpCodes.Ldfld, cecilField: field);
	}

	/// <summary>
	/// Matches the CIL <c>ldsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.CecilField, OpCodes.Ldsfld, cecilField: field);
	}

	/// <summary>
	/// Matches the CIL <c>stfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.CecilField, OpCodes.Stfld, cecilField: field);
	}

	/// <summary>
	/// Matches the CIL <c>stsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Stsfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.CecilField, OpCodes.Stsfld, cecilField: field);
	}

	/// <summary>
	/// Matches the CIL <c>ldflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldflda(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.CecilField, OpCodes.Ldflda, cecilField: field);
	}

	/// <summary>
	/// Matches the CIL <c>ldsflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Ldsflda(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		return new IlPatternElement(IlPatternElementKind.CecilField, OpCodes.Ldsflda, cecilField: field);
	}

	// ======================================================================================
	// fields (convenience overloads)

	/// <summary>
	/// Matches the CIL <c>ldfld</c> instruction for the named instance field declared by <typeparamref name="TDeclaring"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <typeparamref name="TDeclaring"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>ldfld</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldfld<TDeclaring>(string name) where TDeclaring : allows ref struct =>
		reflectionField(OpCodes.Ldfld, @static: false, typeof(TDeclaring), name);

	/// <summary>
	/// Matches the CIL <c>ldfld</c> instruction for the named instance field declared by <paramref name="declaringType"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <paramref name="declaringType"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>ldfld</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldfld(Type declaringType, string name) =>
		reflectionField(OpCodes.Ldfld, @static: false, declaringType, name);

	/// <summary>
	/// Matches the CIL <c>ldfld</c> instruction for the instance field pointed to by <paramref name="field"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>ldfld</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldfld(FieldInfo field) {
		ArgumentNullException.ThrowIfNull(field);
		if (field.IsStatic)
			throw new InvalidOperationException($"field '{field.Name}' of type '{field.DeclaringType}' is a static field, while this instruction expects an instance field");
		return new IlPatternElement(IlPatternElementKind.ReflectionField, OpCodes.Ldfld, reflectionField: field);
	}

	/// <summary>
	/// Matches the CIL <c>ldsfld</c> instruction for the named static field declared by <typeparamref name="TDeclaring"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldsfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <typeparamref name="TDeclaring"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>ldsfld</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldsfld<TDeclaring>(string name) where TDeclaring : allows ref struct =>
		reflectionField(OpCodes.Ldsfld, @static: true, typeof(TDeclaring), name);

	/// <summary>
	/// Matches the CIL <c>ldsfld</c> instruction for the named static field declared by <paramref name="declaringType"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldsfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <paramref name="declaringType"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>ldsfld</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldsfld(Type declaringType, string name) =>
		reflectionField(OpCodes.Ldsfld, @static: true, declaringType, name);


	/// <summary>
	/// Matches the CIL <c>ldsfld</c> instruction for the static field pointed to by <paramref name="field"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldsfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>ldsfld</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldsfld(FieldInfo field) {
		ArgumentNullException.ThrowIfNull(field);
		if (!field.IsStatic)
			throw new InvalidOperationException($"field '{field.Name}' of type '{field.DeclaringType}' is an instance field, while this instruction expects a static field");
		return new IlPatternElement(IlPatternElementKind.ReflectionField, OpCodes.Ldsfld, reflectionField: field);
	}

	/// <summary>
	/// Matches the CIL <c>stfld</c> instruction for the named instance field declared by <typeparamref name="TDeclaring"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Stfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <typeparamref name="TDeclaring"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>stfld</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Stfld<TDeclaring>(string name) where TDeclaring : allows ref struct =>
		reflectionField(OpCodes.Stfld, @static: false, typeof(TDeclaring), name);

	/// <summary>
	/// Matches the CIL <c>stfld</c> instruction for the named instance field declared by <paramref name="declaringType"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Stfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <paramref name="declaringType"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>stfld</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Stfld(Type declaringType, string name) => reflectionField(OpCodes.Stfld, @static: false, declaringType, name);

	/// <summary>
	/// Matches the CIL <c>stfld</c> instruction for the instance field pointed to by <paramref name="field"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Stfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>stfld</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Stfld(FieldInfo field) {
		ArgumentNullException.ThrowIfNull(field);
		if (field.IsStatic)
			throw new InvalidOperationException($"field '{field.Name}' of type '{field.DeclaringType}' is a static field, while this instruction expects an instance field");
		return new IlPatternElement(IlPatternElementKind.ReflectionField, OpCodes.Stfld, reflectionField: field);
	}

	/// <summary>
	/// Matches the CIL <c>stsfld</c> instruction for the named static field declared by <typeparamref name="TDeclaring"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Stsfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <typeparamref name="TDeclaring"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>stsfld</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Stsfld<TDeclaring>(string name) where TDeclaring : allows ref struct =>
		reflectionField(OpCodes.Stsfld, @static: true, typeof(TDeclaring), name);

	/// <summary>
	/// Matches the CIL <c>stsfld</c> instruction for the named static field declared by <paramref name="declaringType"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Stsfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <paramref name="declaringType"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>stsfld</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Stsfld(Type declaringType, string name) =>
		reflectionField(OpCodes.Stsfld, @static: true, declaringType, name);

	/// <summary>
	/// Matches the CIL <c>stsfld</c> instruction for the static field pointed to by <paramref name="field"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Stsfld(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>stsfld</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Stsfld(FieldInfo field) {
		ArgumentNullException.ThrowIfNull(field);
		if (!field.IsStatic)
			throw new InvalidOperationException($"field '{field.Name}' of type '{field.DeclaringType}' is an instance field, while this instruction expects a static field");
		return new IlPatternElement(IlPatternElementKind.ReflectionField, OpCodes.Stsfld, reflectionField: field);
	}

	/// <summary>
	/// Matches the CIL <c>ldflda</c> instruction for the named instance field declared by <typeparamref name="TDeclaring"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldflda(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <typeparamref name="TDeclaring"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>ldflda</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldflda<TDeclaring>(string name) where TDeclaring : allows ref struct =>
		reflectionField(OpCodes.Ldflda, @static: false, typeof(TDeclaring), name);

	/// <summary>
	/// Matches the CIL <c>ldflda</c> instruction for the named instance field declared by <paramref name="declaringType"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldflda(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <paramref name="declaringType"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>ldflda</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldflda(Type declaringType, string name) =>
		reflectionField(OpCodes.Ldflda, @static: false, declaringType, name);

	/// <summary>
	/// Matches the CIL <c>ldflda</c> instruction for the instance field pointed to by <paramref name="field"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldflda(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is a static field; since <c>ldflda</c> operates only on instance fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldflda(FieldInfo field) {
		ArgumentNullException.ThrowIfNull(field);
		if (field.IsStatic)
			throw new InvalidOperationException($"field '{field.Name}' of type '{field.DeclaringType}' is a static field, while this instruction expects an instance field");
		return new IlPatternElement(IlPatternElementKind.ReflectionField, OpCodes.Ldflda, reflectionField: field);
	}

	/// <summary>
	/// Matches the CIL <c>ldsflda</c> instruction for the named static field declared by <typeparamref name="TDeclaring"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldsflda(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <typeparamref name="TDeclaring"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>ldsflda</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldsflda<TDeclaring>(string name) where TDeclaring : allows ref struct =>
		reflectionField(OpCodes.Ldsflda, @static: true, typeof(TDeclaring), name);

	/// <summary>
	/// Matches the CIL <c>ldsflda</c> instruction for the named static field declared by <paramref name="declaringType"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldsflda(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="name"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="MissingFieldException">
	/// Thrown if no field named <paramref name="name"/> is found in <paramref name="declaringType"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>ldsflda</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldsflda(Type declaringType, string name) =>
		reflectionField(OpCodes.Ldsflda, @static: true, declaringType, name);

	/// <summary>
	/// Matches the CIL <c>ldsflda</c> instruction for the static field pointed to by <paramref name="field"/>.
	/// </summary>
	/// <remarks>
	/// This is a reflection-based convenience overload. It's likely to be sufficient for most
	/// cases, but for precise Cecil field reference matching, <see cref="Ldsflda(FieldReference)"/>
	/// should be used.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the field is an instance field; since <c>ldsflda</c> operates only on static fields,
	/// this is most likely misuse / a bug on your end.
	/// </exception>
	public static IlPatternElement Ldsflda(FieldInfo field) {
		ArgumentNullException.ThrowIfNull(field);
		if (!field.IsStatic)
			throw new InvalidOperationException($"field '{field.Name}' of type '{field.DeclaringType}' is an instance field, while this instruction expects a static field");
		return new IlPatternElement(IlPatternElementKind.ReflectionField, OpCodes.Ldsflda, reflectionField: field);
	}

	private static IlPatternElement reflectionField(OpCode opCode, bool @static, Type declaringType, string name) {
		// note: technically, ECMA-335 allows multiple fields with the same name within
		// the same declaring type provided their types differ, C# doesn't allow it though
		// GetField throws AmbiguousMatchException if there are multiple, which in our case
		// is good enough, it's extremely rare that a non-C# assembly is being patched and
		// custom handling logic would probably just throw the same AmbiguousMatchException
		// with a slightly different message
		ArgumentNullException.ThrowIfNull(declaringType);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		FieldInfo field = declaringType.GetField(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) ??
			throw new MissingFieldException(declaringType.FullName, name);
		if (field.IsStatic != @static)
			throw new InvalidOperationException($"field '{name}' of type '{declaringType}' is {(field.IsStatic ? "a static" : "an instance")} field, while this instruction expects {(@static ? "a static" : "an instance")} field");
		return new IlPatternElement(IlPatternElementKind.ReflectionField, opCode, reflectionField: field);
	}

	// ======================================================================================
	// calls

	/// <summary>
	/// Matches the CIL <c>call</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Call(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		return new IlPatternElement(IlPatternElementKind.CecilMethod, OpCodes.Call, cecilMethod: method);
	}

	/// <summary>
	/// Matches the CIL <c>callvirt</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	public static IlPatternElement Callvirt(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		return new IlPatternElement(IlPatternElementKind.CecilMethod, OpCodes.Callvirt, cecilMethod: method);
	}

	// ======================================================================================
	// calls (convenience overloads)

	// TODO: doc comments, i didn't write these yet because i'm kinda tired of writing and
	// copying the boilerplate doc comments on the convenience overloads

	public static IlPatternElement Call<TDeclaring>(string name) => reflectionMethod(OpCodes.Call, typeof(TDeclaring), name, null);
	public static IlPatternElement Call<TDeclaring>(string name, params Type[] parameterTypes) => reflectionMethod(OpCodes.Call, typeof(TDeclaring), name, parameterTypes);
	public static IlPatternElement Call(Type declaringType, string name) => reflectionMethod(OpCodes.Call, declaringType, name, null);
	public static IlPatternElement Call(Type declaringType, string name, params Type[] parameterTypes) => reflectionMethod(OpCodes.Call, declaringType, name, parameterTypes);
	public static IlPatternElement Call(MethodInfo method) {
		ArgumentNullException.ThrowIfNull(method);
		return new IlPatternElement(IlPatternElementKind.ReflectionMethod, OpCodes.Call, reflectionMethod: method);
	}

	public static IlPatternElement Callvirt<TDeclaring>(string name) => reflectionMethod(OpCodes.Callvirt, typeof(TDeclaring), name, null);
	public static IlPatternElement Callvirt<TDeclaring>(string name, params Type[] parameterTypes) => reflectionMethod(OpCodes.Callvirt, typeof(TDeclaring), name, parameterTypes);
	public static IlPatternElement Callvirt(Type declaringType, string name) => reflectionMethod(OpCodes.Callvirt, declaringType, name, null);
	public static IlPatternElement Callvirt(Type declaringType, string name, params Type[] parameterTypes) => reflectionMethod(OpCodes.Callvirt, declaringType, name, parameterTypes);
	public static IlPatternElement Callvirt(MethodInfo method) {
		ArgumentNullException.ThrowIfNull(method);
		return new IlPatternElement(IlPatternElementKind.ReflectionMethod, OpCodes.Callvirt, reflectionMethod: method);
	}

	private static IlPatternElement reflectionMethod(OpCode opCode, Type declaringType, string name, Type[]? parameterTypes) {
		ArgumentNullException.ThrowIfNull(declaringType);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		if (parameterTypes is not null) {
			MethodInfo method = declaringType.GetMethod(
				name,
				flags,
				binder: null,
				types: parameterTypes,
				modifiers: null
			) ?? throw new MissingMethodException(declaringType.FullName, name);
			return new IlPatternElement(IlPatternElementKind.ReflectionMethod, opCode, reflectionMethod: method);
		}

		MethodInfo[] matches = declaringType.GetMethods(flags).Where(m => m.Name == name).ToArray();
		return matches.Length switch {
			1 => new IlPatternElement(IlPatternElementKind.ReflectionMethod, opCode, reflectionMethod: matches[0]),
			0 => throw new MissingMethodException(declaringType.FullName, name),
			_ => throw new AmbiguousMatchException($"method '{declaringType.FullName}.{name}' is overloaded; specify parameter types"),
		};
	}
}
