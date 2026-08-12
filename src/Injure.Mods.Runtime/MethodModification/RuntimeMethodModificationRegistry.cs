// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.MethodModification;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Runtime.MethodModification;

internal sealed class RuntimeMethodModificationRegistry : IDisposable, IStrongRefDroppable {
	private IRuntimeDetourBackend? detourBackend;
	private IRuntimePatchBackend? patchBackend;
	private IAssemblyOwnerResolver? assemblyOwnerResolver;
	private Dictionary<RuntimeMethodTargetKey, DetourTargetState>? detourTargets = new();
	private Dictionary<RuntimeMethodTargetKey, PatchTargetState>? patchTargets = new();
	private int dropped;

	public RuntimeMethodModificationRegistry(IRuntimeDetourBackend detourBackend, IRuntimePatchBackend patchBackend, IAssemblyOwnerResolver assemblyOwnerResolver) {
		InternalStateException.ThrowIfNull(detourBackend);
		InternalStateException.ThrowIfNull(patchBackend);
		InternalStateException.ThrowIfNull(assemblyOwnerResolver);
		this.detourBackend = detourBackend;
		this.patchBackend = patchBackend;
		this.assemblyOwnerResolver = assemblyOwnerResolver;
	}

	public void ReplaceLoad(IReadOnlyCollection<ILoadedCodeMod> mods) => replacePhase(MethodModificationPhase.Load, mods);
	public void ReplaceLink(IReadOnlyCollection<ILoadedCodeMod> mods) => replacePhase(MethodModificationPhase.Link, mods);

	public void ClearAll() {
		List<Exception>? failures = null;
		clearStates(detourTargets?.Values, ref failures);
		detourTargets?.Clear();
		clearStates(patchTargets?.Values, ref failures);
		patchTargets?.Clear();
		if (failures is not null)
			throw new AggregateException("one or more method modification targets failed to clear", failures);
	}

	public void Dispose() => DropStrongReferences();
	public void DropStrongReferences() {
		if (Interlocked.Exchange(ref dropped, 1) != 0)
			return;

		List<Exception>? failures = null;
		dropStates(detourTargets?.Values, ref failures);
		detourTargets?.Clear();
		detourTargets = null;
		dropStates(patchTargets?.Values, ref failures);
		patchTargets?.Clear();
		patchTargets = null;

		detourBackend = null;
		patchBackend = null;
		assemblyOwnerResolver = null;

		if (failures is not null)
			throw new AggregateException("one or more method modification targets failed to drop strong references", failures);
	}

	private void replacePhase(MethodModificationPhase phase, IReadOnlyCollection<ILoadedCodeMod> mods) {
		chk();
		InternalStateException.ThrowIfNull(mods);

		List<RuntimeDetourDeclaration> detours = new();
		List<RuntimePatchDeclaration> patches = new();

		foreach (ILoadedCodeMod mod in mods) {
			detours.AddRange(mod.LoadDetours.Snapshot());
			patches.AddRange(mod.LoadPatches.Snapshot());
			if (phase == MethodModificationPhase.Link) {
				detours.AddRange(mod.LinkDetours.Snapshot());
				patches.AddRange(mod.LinkPatches.Snapshot());
			} else if (phase != MethodModificationPhase.Load) {
				throw new InternalStateException("out of range MethodModificationPhase enum value");
			}
		}

		replacePatchTargets(patches);
		replaceDetourTargets(detours);
	}

	private void replaceDetourTargets(List<RuntimeDetourDeclaration> declarations) {
		IRuntimeDetourBackend currentBackend = detourBackend ??
			throw new InternalStateException("detour backend strong reference has already been dropped");

		Dictionary<RuntimeMethodTargetKey, DetourTargetState> next = new();
		try {
			foreach (IGrouping<RuntimeMethodTargetKey, RuntimeDetourDeclaration> grp in declarations.GroupBy(static declaration => declaration.Target)) {
				RuntimeDetourDeclaration[] sorted = sort(grp);
				DetourTargetState state = new(currentBackend, grp.Key);
				state.Replace(sorted);
				next.Add(grp.Key, state);
			}
		} catch {
			foreach (DetourTargetState state in next.Values)
				bestEffortDrop(state);
			throw;
		}

		Dictionary<RuntimeMethodTargetKey, DetourTargetState> old = detourTargets ??
			throw new InternalStateException("detour target map strong reference has already been dropped");
		detourTargets = next;
		foreach (DetourTargetState state in old.Values)
			state.Clear();
		old.Clear();
	}

	private void replacePatchTargets(List<RuntimePatchDeclaration> declarations) {
		IRuntimePatchBackend currentBackend = patchBackend ??
			throw new InternalStateException("patch backend strong reference has already been dropped");

		Dictionary<RuntimeMethodTargetKey, PatchTargetState> next = new();
		try {
			foreach (IGrouping<RuntimeMethodTargetKey, RuntimePatchDeclaration> grp in declarations.GroupBy(static declaration => declaration.Target)) {
				RuntimePatchDeclaration[] sorted = sort(grp);
				PatchTargetState state = new(currentBackend, grp.Key, getBaselineOwnerId(grp.Key.Method));
				state.Replace(sorted);
				next.Add(grp.Key, state);
			}
		} catch {
			foreach (PatchTargetState state in next.Values)
				bestEffortDrop(state);
			throw;
		}

		Dictionary<RuntimeMethodTargetKey, PatchTargetState> old = patchTargets ??
			throw new InternalStateException("patch target map strong reference has already been dropped");
		patchTargets = next;
		foreach (PatchTargetState state in old.Values)
			state.Clear();
		old.Clear();
	}

	private static T[] sort<T>(IEnumerable<T> declarations) where T : RuntimeMethodModificationDeclaration =>
		OwnerOrderedSorter.Sort(
			declarations.Select(static d => new OwnerOrderedEntry<T>(
					item: d,
					ownerId: d.OwnerId,
					localId: d.Order.LocalId,
					localPriority: d.Order.LocalPriority,
					before: d.Order.Before,
					after: d.Order.After
				)
			).ToArray()
		);

	private string? getBaselineOwnerId(MethodBase targetMethod) {
		IAssemblyOwnerResolver currentResolver = assemblyOwnerResolver ??
			throw new InternalStateException("assembly owner resolver strong reference has already been dropped");
		Assembly? assembly = targetMethod.DeclaringType?.Assembly;
		if (assembly is null)
			return null;
		if (!currentResolver.TryGetOwner(assembly, out string? ownerId))
			return null;
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		return ownerId;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(detourTargets), nameof(patchTargets))]
	private void chk() {
		if (Volatile.Read(ref dropped) != 0 || detourTargets is null || patchTargets is null)
			throw new InternalStateException("runtime method modification registry strong references have already been dropped");
	}

	private static void clearStates<TState>(IEnumerable<TState>? states, ref List<Exception>? failures) where TState : IRuntimeMethodTargetState {
		if (states is null)
			return;
		foreach (TState state in states)
			try {
				state.Clear();
			} catch (Exception ex) {
				(failures ??= new List<Exception>()).Add(ex);
			}
	}

	private static void dropStates<TState>(IEnumerable<TState>? states, ref List<Exception>? failures) where TState : IRuntimeMethodTargetState {
		if (states is null)
			return;
		foreach (TState state in states)
			try {
				state.Dispose();
			} catch (Exception ex) {
				(failures ??= new List<Exception>()).Add(ex);
			} finally {
				try {
					state.DropStrongReferences();
				} catch (Exception ex) {
					(failures ??= new List<Exception>()).Add(ex);
				}
			}
	}

	private static void bestEffortDrop(IRuntimeMethodTargetState state) {
		try { state.Dispose(); } catch {}
		try { state.DropStrongReferences(); } catch {}
	}
}

internal interface IRuntimeMethodTargetState : IDisposable, IStrongRefDroppable {
	void Clear();
}

internal sealed class DetourTargetState(
	IRuntimeDetourBackend backend,
	RuntimeMethodTargetKey target
) : IRuntimeMethodTargetState {
	private IRuntimeDetourBackend? backend = backend;
	private RuntimeMethodTargetKey? target = target;
	private IInstalledRuntimeDetour[] handles = Array.Empty<IInstalledRuntimeDetour>();
	private int dropped;

	public void Replace(IReadOnlyList<RuntimeDetourDeclaration> declarations) {
		chk();
		Clear();
		IRuntimeDetourBackend currentBackend = backend;
		RuntimeMethodTargetKey currentTarget = target.Value;
		List<IInstalledRuntimeDetour> next = new(declarations.Count);
		try {
			foreach (RuntimeDetourDeclaration d in declarations)
				next.Add(
					currentBackend.Install(
						new DetourInstallRequest {
							TargetMethod = currentTarget.Method,
							DetourMethod = d.DetourMethod,
							OwnerId = d.OwnerId,
							LocalId = d.Order.LocalId,
						}
					)
				);
			handles = next.ToArray();
		} catch {
			foreach (IInstalledRuntimeDetour handle in next)
				bestEffortDispose(handle);
			throw;
		}
	}

	public void Clear() {
		IInstalledRuntimeDetour[] h = Interlocked.Exchange(ref handles, Array.Empty<IInstalledRuntimeDetour>());
		List<Exception>? failures = null;
		foreach (IInstalledRuntimeDetour handle in h)
			try {
				handle.Dispose();
				handle.DropStrongReferences();
			} catch (Exception ex) {
				(failures ??= new List<Exception>()).Add(ex);
			}
		if (failures is not null)
			throw new AggregateException("one or more installed detours failed to dispose", failures);
	}

	public void Dispose() => Clear();

	public void DropStrongReferences() {
		if (Interlocked.Exchange(ref dropped, 1) != 0)
			return;
		IInstalledRuntimeDetour[] h = Interlocked.Exchange(ref handles, Array.Empty<IInstalledRuntimeDetour>());
		foreach (IInstalledRuntimeDetour handle in h)
			bestEffortDispose(handle);
		backend = null;
		target = null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(backend), nameof(target))]
	private void chk() {
		if (Volatile.Read(ref dropped) != 0 || backend is null || target is null)
			throw new InternalStateException("detour target state strong references have already been dropped");
	}

	private static void bestEffortDispose(IInstalledRuntimeDetour handle) {
		try { handle.Dispose(); } catch {}
		try { handle.DropStrongReferences(); } catch {}
	}
}

internal sealed class PatchTargetState(
	IRuntimePatchBackend backend,
	RuntimeMethodTargetKey target,
	string? baselineOwnerId
) : IRuntimeMethodTargetState {
	private IRuntimePatchBackend? backend = backend;
	private RuntimeMethodTargetKey? target = target;
	private string? baselineOwnerId = baselineOwnerId;
	private IlManipulatorRegistration[] snapshot = Array.Empty<IlManipulatorRegistration>();
	private IInstalledRuntimePatch? handle;
	private int dropped;

	public void Replace(IReadOnlyList<RuntimePatchDeclaration> declarations) {
		chk();
		Clear();
		snapshot = declarations.Select(static declaration => declaration.Registration).ToArray();
		if (snapshot.Length == 0)
			return;
		handle = backend.InstallPipeline(
			new PatchPipelineInstallRequest {
				TargetMethod = target.Value.Method,
				BaselineOwnerId = baselineOwnerId,
				GetSnapshot = ReadSnapshot,
			}
		);
	}

	public IReadOnlyList<IlManipulatorRegistration> ReadSnapshot() => Volatile.Read(ref snapshot);

	public void Clear() {
		IInstalledRuntimePatch? h = Interlocked.Exchange(ref handle, null);
		h?.Dispose();
		h?.DropStrongReferences();
		snapshot = Array.Empty<IlManipulatorRegistration>();
	}

	public void Dispose() => Clear();

	public void DropStrongReferences() {
		if (Interlocked.Exchange(ref dropped, 1) != 0)
			return;
		IInstalledRuntimePatch? h = Interlocked.Exchange(ref handle, null);
		try { h?.Dispose(); } catch {}
		try { h?.DropStrongReferences(); } catch {}
		snapshot = Array.Empty<IlManipulatorRegistration>();
		backend = null;
		target = null;
		baselineOwnerId = null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(backend), nameof(target))]
	private void chk() {
		if (Volatile.Read(ref dropped) != 0 || backend is null || target is null)
			throw new InternalStateException("patch target state strong references have already been dropped");
	}
}
