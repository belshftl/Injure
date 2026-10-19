// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using Injure.Mods;

namespace Injure.Diagnostics;

/// <summary>
/// Describes the severity of a diagnostic.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DiagnosticSeverity {
	/// <summary>Raw switch tag for <see cref="DiagnosticSeverity"/>.</summary>
	public enum Case {
		/// <summary>
		/// The diagnostic is for debugging, tracing, or other verbose operational detail; the lowest
		/// severity level.
		/// </summary>
		/// <remarks>
		/// The above description is informative/advisory rather than normative.
		/// </remarks>
		Debug = 1,

		/// <summary>
		/// The diagnostic reports normal operation, status, or other information that does not indicate a
		/// problem requiring attention.
		/// </summary>
		/// <remarks>
		/// The above description is informative/advisory rather than normative.
		/// </remarks>
		Info,

		/// <summary>
		/// The diagnostic reports a potentially problematic condition that does not prevent continued
		/// operation, but may warrant investigation or corrective action.
		/// </summary>
		/// <remarks>
		/// The above description is informative/advisory rather than normative.
		/// </remarks>
		Warning,

		/// <summary>
		/// The diagnostic reports a non-fatal failure or invalid condition. The affected operation,
		/// component, or requested functionality did not complete successfully and typically requires
		/// corrective action.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Conditions that prevent the current operation from continuing should generally be represented
		/// by an exception as well as, or instead of, an error diagnostic. Fatal runtime or process
		/// failures should normally be reported through the mechanism responsible for handling that
		/// failure.
		/// </para>
		/// <para>
		/// The above description is informative/advisory rather than normative.
		/// </para>
		/// </remarks>
		Error,
	}
}

/// <summary>
/// Describes one diagnostic event emitted by the engine, the game, or a mod.
/// </summary>
/// <param name="SourceOwnerId">Owner ID of the component that emitted the diagnostic.</param>
/// <param name="Severity">Severity of the diagnostic.</param>
/// <param name="Message">Diagnostic message, as reported by the component.</param>
/// <param name="Generation">
/// Reload generation of the component that emitted the diagnostic, or <see langword="null"/> if the
/// diagnostic was emitted by a component that is not associated with a reload generation.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct DiagnosticEvent(
	string SourceOwnerId,
	DiagnosticSeverity Severity,
	string Message,
	ReloadGeneration? Generation = null
) : IHasInvalidDefault {
	/// <summary>
	/// Throws if this <see cref="DiagnosticEvent"/> is invalid/malformed.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <see cref="SourceOwnerId"/> or <see cref="Message"/> is <see langword="null"/>, or if
	/// <see cref="Generation"/> is non-null but its owner ID is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <see cref="SourceOwnerId"/> is not a valid owner ID, or if <see cref="Generation"/>
	/// is non-null and has a mismatched owner ID.
	/// </exception>
	/// <exception cref="InvalidDefaultException">
	/// Thrown if <see cref="Severity"/> is the <see langword="default"/> value.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Severity"/> is otherwise not a validly constructed closed-enum value.
	/// </exception>
	public void Validate() {
		ModMetadataValidation.ValidateOwnerIdOrThrow(SourceOwnerId);
		ArgumentNullException.ThrowIfNull(Message);
		if (Generation is ReloadGeneration g) {
			ArgumentNullException.ThrowIfNull(g.OwnerId);
			if (g.OwnerId != SourceOwnerId)
				throw new ArgumentException("SourceOwnerId doesn't match reload generation owner");
		}
		_ = Severity.Tag;
	}

	bool IHasInvalidDefault.IsDefault => SourceOwnerId is null;
}
