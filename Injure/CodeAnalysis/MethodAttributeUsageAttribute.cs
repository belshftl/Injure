// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.CodeAnalysis;

/// <summary>
/// Specifies constraints that a method marked with a
/// <see cref="MethodAttributeUsageAttribute"/>-marked attribute must satisfy.
/// </summary>
// this enum's numeric values are mirrored in Injure.Analyzers/Core/Model.cs
[Flags]
public enum MethodConstraints {
	/// <summary>
	/// The method marked with this attribute must be static.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="Instance"/>; specifying both is invalid.
	/// </remarks>
	Static = 1 << 0,

	/// <summary>
	/// The method marked with this attribute must be an instance method, i.e. non-static.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="Static"/>; specifying both is invalid.
	/// </remarks>
	Instance = 1 << 1,

	/// <summary>
	/// The method marked with this attribute must be non-public.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="Public"/>; specifying both is invalid.
	/// </remarks>
	NonPublic = 1 << 2,

	/// <summary>
	/// The method marked with this attribute must be public.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="NonPublic"/>; specifying both is invalid.
	/// </remarks>
	Public = 1 << 3,

	/// <summary>
	/// The method marked with this attribute must be non-generic.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="Generic"/>; specifying both is invalid.
	/// </remarks>
	NonGeneric = 1 << 4,

	/// <summary>
	/// The method marked with this attribute must be generic.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="NonGeneric"/>; specifying both is invalid.
	/// </remarks>
	Generic = 1 << 5,

	/// <summary>
	/// The method marked with this attribute must take in no parameters.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="HasParameters"/>; specifying both is invalid.
	/// </remarks>
	Parameterless = 1 << 6,

	/// <summary>
	/// The method marked with this attribute must take in one or more parameters.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="Parameterless"/>; specifying both is invalid.
	/// </remarks>
	HasParameters = 1 << 7,

	/// <summary>
	/// The method marked with this attribute must have a return type of <c>void</c>.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="ReturnsNonVoid"/>; specifying both is invalid.
	/// </remarks>
	ReturnsVoid = 1 << 8,

	/// <summary>
	/// The method marked with this attribute must have a return type other than <c>void</c>, i.e.
	/// return a value.
	/// </summary>
	/// <remarks>
	/// Contradicts <see cref="ReturnsVoid"/>; specifying both is invalid.
	/// </remarks>
	ReturnsNonVoid = 1 << 9,
}

/// <summary>
/// Specifies what kinds of methods another attribute can be applied to.
/// </summary>
/// <remarks>
/// Informally speaking, this is similar to <see cref="AttributeUsageAttribute"/>, except with
/// constraints specific to method attributes.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MethodAttributeUsageAttribute(MethodConstraints constraints) : Attribute {
	/// <summary>
	/// The declared method constraints.
	/// </summary>
	public MethodConstraints Constraints { get; } = constraints;
}
