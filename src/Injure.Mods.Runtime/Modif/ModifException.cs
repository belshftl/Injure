// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Modif;

/// <summary>
/// Exception thrown if a method could not be transformed.
/// </summary>
/// <remarks>
/// <para>
/// If this was thrown, no method in the pass was installed, so every target's code is still unmodified
/// and running whatever it was running before. The methods that the pass covered are re-marked dirty so
/// that a retry picks them up.
/// </para>
/// </remarks>
internal sealed class ModifException : Exception {
	/// <summary>
	/// The method that was being transformed when the pass failed.
	/// </summary>
	public MethodIdentity Method { get; }

	/// <summary>
	/// The owner responsible, or <see langword="null"/> if the failure is not attributable.
	/// </summary>
	public string? OwnerId { get; }

	/// <summary>
	/// The local ID of the modif responsible, or <see langword="null"/> if the failure is not
	/// attributable.
	/// </summary>
	public string? LocalId { get; }

	public ModifException(MethodIdentity method, Exception ex) : base(describe(method, culpritOf(ex)), ex) {
		Method = method;
		(OwnerId, LocalId) = culpritOf(ex);
	}

	private static (string? OwnerId, string? LocalId) culpritOf(Exception ex) => ex switch {
		IlManipulatorException m => (m.OwnerId, m.LocalId),
		IlPipelineValidationException v => (v.OwnerId, v.LocalId),
		_ => (null, null),
	};

	private static string describe(MethodIdentity method, (string? OwnerId, string? LocalId) culprit) =>
		culprit.OwnerId is null
			? $"failed to transform {method}"
			: $"failed to transform {method}, in manipulator '{culprit.OwnerId}::{culprit.LocalId}'";
}
