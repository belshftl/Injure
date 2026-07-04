// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Input;

public readonly struct GamepadId : IEquatable<GamepadId> {
	internal readonly uint Value; // 0 is invalid
	internal GamepadId(uint value) {
		Value = value;
	}

	public bool Equals(GamepadId other) => Value == other.Value;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is GamepadId other && Equals(other);
	public override int GetHashCode() => unchecked((int)Value);
	public static bool operator ==(GamepadId left, GamepadId right) => left.Value == right.Value;
	public static bool operator !=(GamepadId left, GamepadId right) => left.Value != right.Value;
}
