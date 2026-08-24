// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Pipeline policy configuration.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is the default / recommended settings, that being validate once at the end,
/// attribute a validation failure by re-running, and fail the whole transformation if any manipulator throws.
/// </remarks>
internal readonly struct IlPipelineOptions {
	/// <summary>
	/// Validates after every manipulator instead of once at the end. For tests that need to observe
	/// validation at a specific point; the default policy already attributes failures by re-running.
	/// </summary>
	public bool ValidateAfterEachManipulator { get; init; }

	/// <summary>
	/// Skips validation entirely, leaving <see cref="IlMethodBody.ComputedMaxStack"/> unset.
	/// </summary>
	/// <remarks>
	/// The encoder still analyzes the body, so this only moves where an invalid body is detected and
	/// which exception type reports it. It does not make invalid IL reachable by the runtime.
	/// </remarks>
	public bool SkipFinalValidation { get; init; }

	/// <summary>
	/// Reports a validation failure without attributing it to a particular manipulator.
	/// </summary>
	public bool SkipFailureAttribution { get; init; }

	/// <summary>
	/// Discards a manipulator whose callback throws and continues with the remaining manipulators,
	/// instead of failing the whole transformation.
	/// </summary>
	/// <remarks>
	/// Off by default even though it may be more desirable for a mod loader. A manipulator's edits
	/// are buffered until it returns successfully, so a discarded manipulator leaves no trace in the
	/// working body, but later manipulators may expect to match instructions an earlier one emitted,
	/// so continuing past a failure can produce a body that is valid and secretly wrong.
	/// False is the safer default.
	/// </remarks>
	public bool SkipFailingManipulators { get; init; }
}

/// <summary>
/// The outcome of an IL transformation pipeline.
/// </summary>
/// <param name="Body">
/// If <paramref name="Modified"/> is <see langword="true"/>, the transformed body; otherwise, the
/// baseline instance itself, which the caller must treat as read-only.
/// </param>
/// <param name="Modified">
/// Whether any manipulator committed an edit. If <see langword="false"/>, the caller can skip
/// encoding and ReJIT entirely.
/// </param>
internal readonly record struct IlPipelineResult(IlMethodBody Body, bool Modified);

/// <summary>
/// Runs an ordered set of IL manipulators over one method body.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline clones the baseline once, runs each manipulator in the order given against a
/// transaction over the working body, and validates the result. Manipulators observe the body as
/// each earlier manipulator left it; ordering is decided by the caller.
/// </para>
/// <para>
/// Validation runs once, after the last manipulator. If it fails, the pipeline re-runs the whole
/// sequence from a fresh clone with validation after every manipulator, in order to name the
/// manipulator responsible. This keeps the common case to a single stack analysis while preserving
/// per-manipulator attribution when it matters. The consequence is that a manipulator can be
/// invoked twice for one transformation, so manipulators must not depend on being called exactly
/// once, and any external side effect they perform must tolerate repetition. Attribution can be
/// disabled with <see cref="IlPipelineOptions.SkipFailureAttribution"/>.
/// </para>
/// </remarks>
internal static class IlPipeline {
	/// <summary>
	/// Transforms a baseline method body.
	/// </summary>
	/// <exception cref="IlPipelineValidationException">
	/// Thrown if the transformed body is not a valid CLI method body.
	/// </exception>
	public static IlPipelineResult Transform(
		IlMethodBody baseline,
		IReadOnlyList<IlManipulatorRegistration> manipulators,
		IlOwnerContext ownerContext,
		IIlCallDispatch? callDispatch,
		in IlPipelineOptions options = default
	) {
		InternalStateException.ThrowIfNull(baseline);
		InternalStateException.ThrowIfNull(manipulators);
		if (manipulators.Count == 0)
			return new IlPipelineResult(baseline, false);
		validateUnique(manipulators);

		IlMethodBody working = baseline.Clone();
		bool modified = run(
			working,
			manipulators,
			ownerContext,
			callDispatch,
			options,
			options.ValidateAfterEachManipulator,
			out _
		);
		if (!modified)
			return new IlPipelineResult(baseline, false);

		if (!options.SkipFinalValidation && !options.ValidateAfterEachManipulator) {
			try {
				IlMaxStackAnalyzer.Analyze(working);
			} catch (IlInvalidMethodException ex) {
				throw attribute(baseline, manipulators, ownerContext, callDispatch, options, ex);
			}
		}

		return new IlPipelineResult(working, true);
	}

	private static bool run(
		IlMethodBody working,
		IReadOnlyList<IlManipulatorRegistration> manipulators,
		IlOwnerContext ownerContext,
		IIlCallDispatch? callDispatch,
		in IlPipelineOptions options,
		bool validateEachStep,
		out IlManipulatorRegistration? culprit
	) {
		culprit = null;
		bool modified = false;
		foreach (IlManipulatorRegistration registration in manipulators) {
			InternalStateException.ThrowIfNull(registration);
			InternalStateException.ThrowIfInvalidOwnerId(registration.OwnerId);
			InternalStateException.ThrowIfInvalidLocalId(registration.LocalId);
			IlTransactionCore core = new(working, registration.OwnerId, registration.LocalId, ownerContext, callDispatch);
			try {
				registration.Invoke(core);
			} catch (Exception) when (options.SkipFailingManipulators) {
				core.Abort();
				continue;
			} catch (Exception ex) {
				core.Abort();
				throw new IlManipulatorException(registration.OwnerId, registration.LocalId, ex);
			}
			if (!core.HasPendingEdits) {
				core.Abort();
				continue;
			}
			core.Commit();
			modified = true;

			if (!validateEachStep)
				continue;
			try {
				IlMaxStackAnalyzer.Analyze(working);
			} catch (IlInvalidMethodException) {
				culprit = registration;
				throw;
			}
		}
		return modified;
	}

	/// <summary>
	/// Re-runs the sequence with per-step validation to name the manipulator responsible for a
	/// validation failure.
	/// </summary>
	private static IlPipelineValidationException attribute(
		IlMethodBody baseline,
		IReadOnlyList<IlManipulatorRegistration> manipulators,
		IlOwnerContext ownerContext,
		IIlCallDispatch? callDispatch,
		in IlPipelineOptions options,
		IlInvalidMethodException failure
	) {
		if (options.SkipFailureAttribution)
			return new IlPipelineValidationException(failure.Message, null, null, failure);

		IlManipulatorRegistration? culprit = null;
		IlInvalidMethodException attributed = failure;
		try {
			run(baseline.Clone(), manipulators, ownerContext, callDispatch, options, validateEachStep: true, out culprit);
		} catch (IlInvalidMethodException ex) {
			attributed = ex;
		} catch (Exception) {
			// re-run diverged from the original run
			return new IlPipelineValidationException(failure.Message, null, null, failure);
		}

		return culprit is null
			? new IlPipelineValidationException(failure.Message, null, null, failure)
			: new IlPipelineValidationException(attributed.Message, culprit.OwnerId, culprit.LocalId, attributed);
	}

	private static void validateUnique(IReadOnlyList<IlManipulatorRegistration> manipulators) {
		HashSet<(string OwnerId, string LocalId)> seen = new(manipulators.Count);
		foreach (IlManipulatorRegistration registration in manipulators) {
			InternalStateException.ThrowIfNull(registration);
			InternalStateException.ThrowIfInvalidOwnerId(registration.OwnerId);
			InternalStateException.ThrowIfInvalidLocalId(registration.LocalId);
			if (!seen.Add((registration.OwnerId, registration.LocalId)))
				throw new InternalStateException($"manipulator '{registration.OwnerId}::{registration.LocalId}' got registered more than once");
		}
	}
}
