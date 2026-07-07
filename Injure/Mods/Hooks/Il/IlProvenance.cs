// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Hooks.Il;

/// <summary>
/// The provenance of an IL instruction; that is, an identification of the owner that most recently
/// introduced or modified it.
/// </summary>
/// <remarks>
/// <para>
/// Provenance is for composition, diagnostics, and pattern matching; it is not a security feature.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is the unknown-provenance value.
/// </para>
/// </remarks>
public readonly struct IlProvenance : IEquatable<IlProvenance> {
	/// <summary>
	/// The owner that most recently introduced or modified the instruction, or
	/// <see langword="null"/> if the instruction's provenance is unknown.
	/// </summary>
	public string? OwnerId { get; }

	internal IlProvenance(string? ownerId) {
		if (ownerId is not null && !ModMetadataValidation.ValidateOwnerId(ownerId, out string? e))
			throw new InternalStateException($"IlProvenance creation got passed invalid owner ID '{ownerId}': {e}");
		OwnerId = ownerId;
	}

	public bool Equals(IlProvenance other) => OwnerId == other.OwnerId;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlProvenance other && Equals(other);
	public override int GetHashCode() => OwnerId is null ? 0 : StringComparer.Ordinal.GetHashCode(OwnerId);
	public static bool operator ==(IlProvenance left, IlProvenance right) => left.Equals(right);
	public static bool operator !=(IlProvenance left, IlProvenance right) => !left.Equals(right);

	public override string ToString() => OwnerId ?? "<unknown provenance>";
}
