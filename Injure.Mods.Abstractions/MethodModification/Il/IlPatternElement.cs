// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal enum IlPatternElementKind {
	UninitializedValue = 0,
	Any = 1,

	// raw
	OpCode,

	// loading literals
	LdcI4,
	LdcI8,
	LdcR4,
	LdcR8,
	Ldstr,

	// args
	Ldarg,
	Ldarga,
	Starg,

	// locals
	Ldloc,
	Ldloca,
	Stloc,

	// fields
	CecilField,
	ReflectionField,

	// calls
	CecilMethod,
	ReflectionMethod,
}

internal static class IlPatternElementKindExtensions {
	extension(IlPatternElementKind k) {
		public bool MatchesEquivalentShorterForms => k switch {
			IlPatternElementKind.Any => false,

			IlPatternElementKind.OpCode => false,

			IlPatternElementKind.LdcI4 => true,
			IlPatternElementKind.LdcI8 => false,
			IlPatternElementKind.LdcR4 => false,
			IlPatternElementKind.LdcR8 => false,
			IlPatternElementKind.Ldstr => false,

			IlPatternElementKind.Ldarg => true,
			IlPatternElementKind.Ldarga => true,
			IlPatternElementKind.Starg => true,

			IlPatternElementKind.Ldloc => true,
			IlPatternElementKind.Ldloca => true,
			IlPatternElementKind.Stloc => true,

			IlPatternElementKind.CecilField => false,
			IlPatternElementKind.ReflectionField => false,

			IlPatternElementKind.CecilMethod => false,
			IlPatternElementKind.ReflectionMethod => false,

			_ => false,
		};
	}
}

/// <summary>
/// Opaque value describing one instruction match pattern in an IL match pattern.
/// </summary>
/// <remarks>
/// <para>
/// Instances are normally created through <see cref="MatchIl"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct IlPatternElement {
	internal IlPatternElementKind Kind { get; }
	internal OpCode OpCode { get; }
	internal MethodReference? CecilMethod { get; }
	internal MethodInfo? ReflectionMethod { get; }
	internal FieldReference? CecilField { get; }
	internal FieldInfo? ReflectionField { get; }
	internal int Int { get; }
	internal long Long { get; }
	internal float Float { get; }
	internal double Double { get; }
	internal string? String { get; }

	internal bool IsValid => Kind != IlPatternElementKind.UninitializedValue;

	internal IlPatternElement(
		IlPatternElementKind kind,
		OpCode opCode = default,
		MethodReference? cecilMethod = null,
		MethodInfo? reflectionMethod = null,
		FieldReference? cecilField = null,
		FieldInfo? reflectionField = null,
		int @int = 0,
		long @long = 0,
		float @float = 0f,
		double @double = 0.0,
		string? @string = null
	) {
		Kind = kind;
		OpCode = opCode;
		CecilMethod = cecilMethod;
		ReflectionMethod = reflectionMethod;
		CecilField = cecilField;
		ReflectionField = reflectionField;
		Int = @int;
		Long = @long;
		Float = @float;
		Double = @double;
		String = @string;
	}
}
