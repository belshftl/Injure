// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Injure.Input;

/// <summary>
/// A position in an <see cref="IInputSource"/>'s event history.
/// </summary>
/// <remarks>
/// <para>
/// Created by <see cref="IInputSource.CreateCursor"/>, and only usable with the source that created
/// it. A cursor is a plain value: copying it creates an independent cursor at the same position,
/// and it doesn't need to be disposed.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid; every source rejects it.
/// </para>
/// </remarks>
public readonly struct InputCursor : IEquatable<InputCursor> {
	private readonly object? sourceToken;
	internal readonly ulong Seq;

	internal InputCursor(object sourceToken, ulong seq) {
		this.sourceToken = sourceToken;
		Seq = seq;
	}

	internal bool BelongsTo(object token) => ReferenceEquals(sourceToken, token);

	/// <summary>Whether this and <paramref name="other"/> are the same value.</summary>
	public bool Equals(InputCursor other) => ReferenceEquals(sourceToken, other.sourceToken) && Seq == other.Seq;
	/// <summary>
	/// Equivalent to <see cref="Equals(InputCursor)"/> if <paramref name="obj"/> is a
	/// <see cref="InputCursor"/>; otherwise <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is InputCursor other && Equals(other);
	/// <summary>Computes a hash consistent with <see cref="Equals(InputCursor)"/>.</summary>
	public override int GetHashCode() {
		int sourceHash = sourceToken is null ? 0 : RuntimeHelpers.GetHashCode(sourceToken);
		return HashCode.Combine(sourceHash, Seq);
	}
	/// <summary>Equivalent to <see cref="Equals(InputCursor)"/>.</summary>
	public static bool operator ==(InputCursor left, InputCursor right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(InputCursor)"/>.</summary>
	public static bool operator !=(InputCursor left, InputCursor right) => !left.Equals(right);
}
