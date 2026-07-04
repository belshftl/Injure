// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Input;

public readonly struct ActionId : IEquatable<ActionId> {
	public bool IsValid => Value != 0;
	internal readonly uint Value;
	internal ActionId(uint value) {
		Value = value;
	}

	public bool Equals(ActionId other) => Value == other.Value;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is ActionId other && Equals(other);
	public override int GetHashCode() => unchecked((int)Value);
	public static bool operator ==(ActionId left, ActionId right) => left.Value == right.Value;
	public static bool operator !=(ActionId left, ActionId right) => left.Value != right.Value;
}
