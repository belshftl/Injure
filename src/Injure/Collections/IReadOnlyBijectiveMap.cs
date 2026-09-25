// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Collections;

/// <summary>
/// A read-only view of a <typeparamref name="TLeft"/> &lt;-&gt; <typeparamref name="TRight"/> bijection.
/// </summary>
/// <typeparam name="TLeft">Type of the left side of a pair.</typeparam>
/// <typeparam name="TRight">Type of the right side of a pair.</typeparam>
/// <remarks>
/// <para>
/// <typeparamref name="TLeft"/> / <typeparamref name="TRight"/> should have well-defined equality
/// or be supplied with comparers with well-defined equality.
/// </para>
/// <para>
/// Enumeration yields every pair exactly once, in unspecified order.
/// </para>
/// </remarks>
public interface IReadOnlyBijectiveMap<TLeft, TRight>
	: IReadOnlyCollection<(TLeft Left, TRight Right)> where TLeft : notnull where TRight : notnull {
	/// <summary>
	/// Checks whether a pair with this left value is present.
	/// </summary>
	bool ContainsLeft(TLeft left);

	/// <summary>
	/// Checks whether a pair with this right value is present.
	/// </summary>
	bool ContainsRight(TRight right);

	/// <summary>
	/// Looks up the right value paired with <paramref name="left"/>.
	/// </summary>
	/// <param name="left">Left value to look up.</param>
	/// <param name="right">Corresponding right value, or <see langword="default"/> if there is none.</param>
	bool TryGetByLeft(TLeft left, [NotNullWhen(true)] out TRight? right);

	/// <summary>
	/// Looks up the left value paired with <paramref name="right"/>.
	/// </summary>
	/// <param name="right">Right value to look up.</param>
	/// <param name="left">Corresponding left value, or <see langword="default"/> if there is none.</param>
	bool TryGetByRight(TRight right, [NotNullWhen(true)] out TLeft? left);

	/// <summary>
	/// Gets the right value paired with <paramref name="left"/>.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thorwn if there is no pair with this left value.
	/// </exception>
	TRight GetByLeft(TLeft left);

	/// <summary>
	/// Gets the left value paired with <paramref name="right"/>.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if no pair has this right value.
	/// </exception>
	TLeft GetByRight(TRight right);
}
