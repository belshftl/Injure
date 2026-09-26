// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// A provenance constraint on an IL instruction/pattern match operation. See <see cref="IlProvenance"/>
/// for an explanation of provenance.
/// </summary>
/// <remarks>
/// <para>
/// This is required for IL matching, not merely an additional feature to narrow searches.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid. See <see cref="Any"/> if you genuinely don't
/// care about provenance; most of the time, you do, even if you don't realize it.
/// </para>
/// </remarks>
public readonly struct IlProvenanceConstr {
	internal enum ConstraintKind {
		UninitializedValue = 0,
		Any,
		AllFromOwner,
		AllUnknown,
		AllUniform,
	}

	internal readonly ConstraintKind Kind;
	internal readonly string? OwnerId;

	private IlProvenanceConstr(ConstraintKind kind, string? ownerId) {
		Kind = kind;
		OwnerId = ownerId;
	}

	/// <summary>
	/// Matches regardless of instruction provenance.
	/// </summary>
	public static IlProvenanceConstr Any { get; } = new(ConstraintKind.Any, null);

	/// <summary>
	/// Requires every instruction in the matched range to have the specified provenance.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="ownerId"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID.
	/// </exception>
	public static IlProvenanceConstr AllFromOwner(string ownerId) {
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		return new IlProvenanceConstr(ConstraintKind.AllFromOwner, ownerId);
	}

	/// <summary>
	/// Requires every instruction in the matched range to have unknown provenance.
	/// </summary>
	public static IlProvenanceConstr AllUnknown { get; } = new(ConstraintKind.AllUnknown, null);

	/// <summary>
	/// Requires every instruction in the matched range to have the same known provenance.
	/// </summary>
	/// <remarks>
	/// An all-unknown-provenance range does not satisfy this constraint. This is, notably, distinct from the
	/// behavior of <see cref="IlMatch.TryGetUniformProvenance(out IlProvenance)"/>, which considers all-unknown
	/// a successful uniform match.
	/// </remarks>
	public static IlProvenanceConstr AllUniform { get; } = new(ConstraintKind.AllUniform, null);
}
