// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

internal sealed class IlPipelineResult : IDisposable, IStrongRefDroppable {
	private MethodDefinition? method;
	private Dictionary<Instruction, InternalIlProvenance>? provenance;
	private IDisposable[] retentions;

	public MethodDefinition Method => method ?? throw new InternalStateException("IL pipeline result got used after its strong refs have been dropped");

	internal IlPipelineResult(MethodDefinition method, Dictionary<Instruction, InternalIlProvenance> provenance, IReadOnlyList<IDisposable> retentions) {
		this.method = method ?? throw new InternalStateException("IlPipelineResult constructed with null method definition");
		this.provenance = provenance ?? throw new InternalStateException("IlPipelineResult constructed with null provenance dictionary");
		this.retentions = retentions?.ToArray() ?? throw new InternalStateException("IlPipelineResult constructed with null retentions array");
	}

	public IlProvenance GetProvenance(Instruction instr) {
		if (instr is null)
			throw new InternalStateException("IlPipelineResult.GetProvenance() got passed null instruction");
		Dictionary<Instruction, InternalIlProvenance> p = provenance ?? throw new InternalStateException("IL pipeline result got used after its strong refs have been dropped");
		if (!p.TryGetValue(instr, out InternalIlProvenance value))
			throw new InternalStateException("instruction doesn't belong to this pipeline result");
		return new IlProvenance(value.OwnerId);
	}

	public bool TryGetProvenance(Instruction instr, out IlProvenance value) {
		if (instr is null)
			throw new InternalStateException("IlPipelineResult.GetProvenance() got passed null instruction");
		Dictionary<Instruction, InternalIlProvenance> p = provenance ?? throw new InternalStateException("IL pipeline result got used after its strong refs have been dropped");
		if (p.TryGetValue(instr, out InternalIlProvenance internalValue)) {
			value = new IlProvenance(internalValue.OwnerId);
			return true;
		}
		value = default;
		return false;
	}

	public void Dispose() {
		method = null;
		provenance?.Clear();
		provenance = null;
		IDisposable[] r = Interlocked.Exchange(ref retentions, Array.Empty<IDisposable>());
		List<Exception>? failures = null;
		foreach (IDisposable retention in r) {
			try {
				retention.Dispose();
			} catch (Exception ex) {
				(failures ??= new List<Exception>()).Add(ex);
			}
		}
		if (failures is not null)
			throw new AggregateException("one or more managed-delegate retention leases threw during dispose", failures);
	}

	public void DropStrongReferences() {
		method = null;
		provenance?.Clear();
		provenance = null;
		IDisposable[] r = Interlocked.Exchange(ref retentions, Array.Empty<IDisposable>());
		foreach (IDisposable retention in r)
			try { retention?.Dispose(); } catch {}
	}
}

internal static class IlPipelineRunner {
	public static IlPipelineResult Transform(
		MethodDefinition method,
		string? baselineOwnerId,
		IReadOnlyList<IlManipulatorRegistration> manipulators,
		IIlManagedDelegateLowerer? managedDelegateLowerer,
		IIlBackendOperandNormalizer operandNormalizer
	) {
		InternalStateException.ThrowIfNull(method);
		InternalStateException.ThrowIfNull(manipulators);
		if (!method.HasBody)
			throw new InternalStateException("IlPipelineRunner got passed method with no body");
		if (baselineOwnerId is not null && !ModMetadataValidation.ValidateOwnerId(baselineOwnerId, out string? e))
			throw new InternalStateException($"IlPipelineRunner got passed a bad owner ID for the baseline: {e}");

		HashSet<(string OwnerId, string LocalId)> identities = new();
		foreach (IlManipulatorRegistration manipulator in manipulators) {
			ArgumentNullException.ThrowIfNull(manipulator);
			if (!identities.Add((manipulator.OwnerId, manipulator.LocalId)))
				throw new IlPipelineException($"duplicate manipulator identity '{manipulator.OwnerId}::{manipulator.LocalId}'");
		}

		List<IDisposable> retentions = new();
		try {
			var working = IlWorkingBody.Clone(method, new InternalIlProvenance(baselineOwnerId, null), operandNormalizer);
			foreach (IlManipulatorRegistration m in manipulators) {
				IlSnapshot snap = working.CaptureSnapshot();
				IlTransactionCore txn = new(working, snap, new InternalIlProvenance(m.OwnerId, m.LocalId), managedDelegateLowerer, retentions);
				try {
					m.Invoke(txn);
					txn.Commit();
				} catch (IlPipelineException) {
					throw;
				} catch (Exception ex) {
					if (ExceptionPolicy.IsInternalState(ex))
						throw;
					throw new IlPipelineException($"IL manipulator '{m.OwnerId}::{m.LocalId}' threw", ExceptionSnapshot.FromException(ex).ToException());
				} finally {
					txn.DropStrongReferences();
				}
			}

			method.Body = working.Body;
			return new IlPipelineResult(method, working.CopyProvenance(), retentions);
		} catch {
			for (int i = retentions.Count - 1; i >= 0; i--) {
				try {
					retentions[i].Dispose();
				} catch {
					// TODO
				}
			}
			throw;
		}
	}
}
