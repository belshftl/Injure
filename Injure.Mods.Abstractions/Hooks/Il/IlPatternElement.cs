// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

internal enum IlPatternElementKind {
	UninitializedValue = 0,
	Any = 1,
	OpCode,
	Call,
	Callvirt,
	Field,
	Ldarg,
	LdcI4,
	Ldloc,
	Stloc,
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
	internal MethodReference? Method { get; }
	internal FieldReference? Field { get; }
	internal int Integer { get; }

	internal bool IsValid => Kind != IlPatternElementKind.UninitializedValue;

	internal IlPatternElement(IlPatternElementKind kind, OpCode opCode = default, MethodReference? method = null, FieldReference? field = null, int integer = 0) {
		Kind = kind;
		OpCode = opCode;
		Method = method;
		Field = field;
		Integer = integer;
	}
}
