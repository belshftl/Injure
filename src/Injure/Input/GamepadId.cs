// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Injure.Input;

/// <summary>
/// Identifies a connected gamepad across all host event sources in the process.
/// </summary>
/// <remarks>
/// <para>
/// IDs come from a single process-wide allocator (<see cref="Allocate"/>), so IDs minted by
/// different event sources do not collide. A gamepad that is disconnected and reconnected gets a
/// new ID.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct GamepadId : IEquatable<GamepadId> {
	private static int next = 0;

	internal readonly uint Value; // 0 is invalid

	private GamepadId(uint value) {
		Value = value;
	}

	/// <summary>
	/// Whether this is a valid ID, i.e. not <see langword="default"/>.
	/// </summary>
	public bool IsValid => Value != 0;

	/// <summary>
	/// Allocates a new, process-wide unique ID.
	/// </summary>
	/// <remarks>
	/// Thread-safe. Meant to be called by whatever reports the gamepad, typically an event source
	/// implementation.
	/// </remarks>
	public static GamepadId Allocate() => new(unchecked((uint)Interlocked.Increment(ref next)));

	public bool Equals(GamepadId other) => Value == other.Value;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is GamepadId other && Equals(other);
	public override int GetHashCode() => unchecked((int)Value);
	public static bool operator ==(GamepadId left, GamepadId right) => left.Value == right.Value;
	public static bool operator !=(GamepadId left, GamepadId right) => left.Value != right.Value;

	/// <summary>
	/// Returns a string of the form <c>gamepad#N</c>, or <c>gamepad#invalid</c> for the
	/// <see langword="default"/> value.
	/// </summary>
	public override string ToString() => Value != 0 ? "gamepad#" + Value.ToString(CultureInfo.InvariantCulture) : "gamepad#invalid";
}
