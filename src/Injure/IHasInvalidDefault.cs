// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.CodeAnalysis;

namespace Injure;

/// <summary>
/// Signifies that the marked struct's <see langword="default"/> value is not a logically valid
/// value of that struct.
/// </summary>
/// <remarks>
/// <para>
/// Members of an implementing type throw <see cref="InvalidDefaultException"/> when used on the
/// default value, and methods taking one throw <see cref="ArgumentInvalidDefaultException"/> when
/// passed it; see <see cref="ArgumentInvalidDefaultException.ThrowIfDefault{T}(T, string?)"/>.
/// </para>
/// <para>
/// Implementing types implement <see cref="IsDefault"/> explicitly, so it's only reachable through
/// the interface, typically as a generic constraint (<c>where T : struct, IHasInvalidDefault</c>).
/// Code that needs an optional value should use <see cref="Nullable{T}"/> rather than the default
/// value.
/// </para>
/// </remarks>
[InterfaceImplKindConstraint(InterfaceImplKind.Struct)]
public interface IHasInvalidDefault {
	/// <summary>
	/// Whether this is the <see langword="default"/> value of its type.
	/// </summary>
	/// <remarks>
	/// Never throws, including on the default value.
	/// </remarks>
	bool IsDefault { get; }
}
