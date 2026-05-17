// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Input;

public readonly struct ActionID : IEquatable<ActionID> {
	public bool IsValid => Value != 0;
	internal readonly uint Value;
	internal ActionID(uint value) => Value = value;

	public bool Equals(ActionID other) => Value == other.Value;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is ActionID other && Equals(other);
	public override int GetHashCode() => unchecked((int)Value);
	public static bool operator ==(ActionID left, ActionID right) => left.Value == right.Value;
	public static bool operator !=(ActionID left, ActionID right) => left.Value != right.Value;
}
