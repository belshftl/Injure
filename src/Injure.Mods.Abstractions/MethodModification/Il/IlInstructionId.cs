// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Identifies an instruction within a method body.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct IlInstructionId : IEquatable<IlInstructionId> {
	/// <summary>
	/// The method-local ID value.
	/// </summary>
	public ulong Value { get; }

	/// <summary>
	/// Whether this value refers to an instruction.
	/// </summary>
	public bool IsValid => Value > 0;

	internal IlInstructionId(ulong value) => Value = value;

	public bool Equals(IlInstructionId other) => Value == other.Value;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlInstructionId other && Equals(other);
	public override int GetHashCode() => Value.GetHashCode();
	public static bool operator ==(IlInstructionId left, IlInstructionId right) => left.Equals(right);
	public static bool operator !=(IlInstructionId left, IlInstructionId right) => !left.Equals(right);

	/// <summary>
	/// Formats this <see cref="IlInstructionId"/> into a string in the form <c>I123</c>.
	/// </summary>
	public override string ToString() => "I" + Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
