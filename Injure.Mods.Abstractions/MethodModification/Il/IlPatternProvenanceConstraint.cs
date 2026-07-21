// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// A provenance constraint on an IL instruction/pattern match operation.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct IlPatternProvenanceConstraint {
	internal enum ConstraintKind {
		UninitializedValue = 0,
		Any,
		AllFromOwner,
		AllUnknown,
		AllUniform,
	}

	internal readonly ConstraintKind Kind;
	internal readonly string? OwnerId;

	private IlPatternProvenanceConstraint(ConstraintKind kind, string? ownerId) {
		Kind = kind;
		OwnerId = ownerId;
	}

	/// <summary>
	/// Matches regardless of instruction provenance.
	/// </summary>
	public static IlPatternProvenanceConstraint Any { get; } = new(ConstraintKind.Any, null);

	/// <summary>
	/// Requires every instruction in the matched range to have the specified provenance.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="ownerId"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID.
	/// </exception>
	public static IlPatternProvenanceConstraint AllFromOwner(string ownerId) {
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		return new IlPatternProvenanceConstraint(ConstraintKind.AllFromOwner, ownerId);
	}

	/// <summary>
	/// Requires every instruction in the matched range to have unknown provenance.
	/// </summary>
	public static IlPatternProvenanceConstraint AllUnknown { get; } = new(ConstraintKind.AllUnknown, null);

	/// <summary>
	/// Requires every instruction in the matched range to have the same known provenance.
	/// </summary>
	/// <remarks>
	/// An all-unknown-provenance range does not satisfy this constraint.
	/// </remarks>
	public static IlPatternProvenanceConstraint AllUniform { get; } = new(ConstraintKind.AllUniform, null);
}
