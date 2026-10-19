// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.CodeAnalysis;

/// <summary>
/// What kinds of types can implement an interface marked with
/// <see cref="InterfaceImplKindConstraintAttribute"/>.
/// </summary>
// this enum's numeric values are mirrored in eng/Analyzers/Injure.AnalyzerShared/Usage/InterfaceImplKindAnalyzerBase.cs
public enum InterfaceImplKind {
	/// <summary>
	/// Only classes, including record classes, can implement the interface.
	/// </summary>
	Class = 1,

	/// <summary>
	/// Only structs, including record structs and ref structs, can implement the interface.
	/// </summary>
	Struct = 2,
}

/// <summary>
/// Restricts what kinds of types (classes or structs) can implement the marked interface, enforced
/// by the <c>Injure.Analyzers</c> analyzer.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class InterfaceImplKindConstraintAttribute(InterfaceImplKind kind) : Attribute {
	/// <summary>
	/// What kind of type can implement the marked interface.
	/// </summary>
	public InterfaceImplKind Kind { get; } = kind;
}
