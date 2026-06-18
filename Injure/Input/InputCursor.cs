// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Injure.Input;

/// <summary>
/// Identifies a position in an input source's bounded event history.
/// </summary>
/// <remarks>
/// <para>
/// Cursors are created by an <see cref="IInputSource"/> and may only be used with
/// the source that created them.
/// </para>
/// <para>
/// Copying a cursor creates an independent cursor at the same position.
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

	public bool Equals(InputCursor other) => ReferenceEquals(sourceToken, other.sourceToken) && Seq == other.Seq;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is InputCursor other && Equals(other);
	public override int GetHashCode() {
		int sourceHash = sourceToken is null ? 0 : RuntimeHelpers.GetHashCode(sourceToken);
		return HashCode.Combine(sourceHash, Seq);
	}
	public static bool operator ==(InputCursor left, InputCursor right) => left.Equals(right);
	public static bool operator !=(InputCursor left, InputCursor right) => !left.Equals(right);
}
