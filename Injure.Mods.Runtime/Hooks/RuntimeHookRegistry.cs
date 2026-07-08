// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Hooks;
using Injure.Mods.Abstractions.Hooks.Il;

namespace Injure.Mods.Runtime.Hooks;

internal sealed class RuntimeHookRegistry : IDisposable {
	private IRuntimeHookBackend? backend;
	private IAssemblyOwnerResolver? assemblyOwnerResolver;
	private Dictionary<RuntimeHookTargetKey, ManagedHookTargetState>? managedTargets = new();
	private Dictionary<RuntimeHookTargetKey, IlHookTargetState>? ilTargets = new();
	private int disposed = 0;

	public RuntimeHookRegistry(IRuntimeHookBackend backend, IAssemblyOwnerResolver assemblyOwnerResolver) {
		InternalStateException.ThrowIfNull(backend);
		InternalStateException.ThrowIfNull(assemblyOwnerResolver);
		this.backend = backend;
		this.assemblyOwnerResolver = assemblyOwnerResolver;
	}

	public void ReplaceLoadHooks(IReadOnlyCollection<ILoadedCodeMod> mods) =>
		replacePhase(HookDeclarationPhase.Load, mods);

	private void replacePhase(HookDeclarationPhase phase, IReadOnlyCollection<ILoadedCodeMod> mods) {
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("this RuntimeHookRegistry has already been disposed/dropped");
		InternalStateException.ThrowIfNull(mods);

		List<ManagedHookDeclaration> managed = new();
		List<IlHookDeclaration> il = new();

		foreach (ILoadedCodeMod mod in mods) {
			RuntimeHookDeclarationSet set = phase switch {
				HookDeclarationPhase.Load => mod.LoadHooks,
				HookDeclarationPhase.Link => mod.LinkHooks,
				_ => throw new InternalStateException("out of range HookDeclarationPhase enum value"),
			};

			foreach (RuntimeHookDeclaration decl in set.Snapshot()) {
				switch (decl) {
				case ManagedHookDeclaration managedDecl:
					managed.Add(managedDecl);
					break;
				case IlHookDeclaration ilDecl:
					il.Add(ilDecl);
					break;
				default:
					throw new InternalStateException($"unknown hook declaration type '{decl.GetType().FullName}'");
				}
			}
		}

		replaceManagedTargets(managed);
		replaceIlTargets(il);
	}

	public void ClearAll() {
		if (managedTargets is not null) {
			foreach (ManagedHookTargetState state in managedTargets.Values)
				state.Clear();
			managedTargets.Clear();
		}

		if (ilTargets is not null) {
			foreach (IlHookTargetState state in ilTargets.Values)
				state.Clear();
			ilTargets.Clear();
		}
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		List<Exception>? failures = null;

		backend = null;
		assemblyOwnerResolver = null;

		if (managedTargets is not null) {
			foreach (ManagedHookTargetState state in managedTargets.Values) {
				try {
					state.Dispose();
				} catch (Exception ex) {
					(failures ??= new List<Exception>()).Add(ex);
				} finally {
					state.DropStrongReferences();
				}
			}
			managedTargets.Clear();
		}
		managedTargets = null;

		if (ilTargets is not null) {
			foreach (IlHookTargetState state in ilTargets.Values) {
				try {
					state.Dispose();
				} catch (Exception ex) {
					(failures ??= new List<Exception>()).Add(ex);
				} finally {
					state.DropStrongReferences();
				}
			}
			ilTargets.Clear();
		}
		ilTargets = null;

		if (failures is not null)
			throw new AggregateException("one or more targets failed to dispose", failures);
	}

	private void replaceManagedTargets(List<ManagedHookDeclaration> decls) {
		if (backend is null)
			throw new InternalStateException("this RuntimeHookRegistry has already been disposed/dropped");

		Dictionary<RuntimeHookTargetKey, ManagedHookTargetState> next = new();
		foreach (IGrouping<RuntimeHookTargetKey, ManagedHookDeclaration> grp in decls.GroupBy(static d => d.Target)) {
			ManagedHookDeclaration[] sorted = sort(grp);
			ManagedHookTargetState state = new(backend, grp.Key);
			state.Replace(sorted);
			next.Add(grp.Key, state);
		}

		Dictionary<RuntimeHookTargetKey, ManagedHookTargetState> old = managedTargets!;
		managedTargets = next;
		foreach (ManagedHookTargetState state in old.Values)
			state.Clear();
		old.Clear();
	}

	private void replaceIlTargets(List<IlHookDeclaration> decls) {
		if (backend is null)
			throw new InternalStateException("this RuntimeHookRegistry has already been disposed/dropped");

		Dictionary<RuntimeHookTargetKey, IlHookTargetState> next = new();
		foreach (IGrouping<RuntimeHookTargetKey, IlHookDeclaration> grp in decls.GroupBy(static d => d.Target)) {
			IlHookDeclaration[] sorted = sort(grp);
			IlHookTargetState state = new(backend, grp.Key, getBaselineOwnerId(grp.Key.Method));
			state.Replace(sorted);
			next.Add(grp.Key, state);
		}

		Dictionary<RuntimeHookTargetKey, IlHookTargetState> old = ilTargets!;
		ilTargets = next;
		foreach (IlHookTargetState state in old.Values)
			state.Clear();
		old.Clear();
	}

	private static T[] sort<T>(IEnumerable<T> decls) where T : RuntimeHookDeclaration =>
		OwnerOrderedSorter.Sort(decls.Select(static d => new OwnerOrderedEntry<T>(
			item: d,
			ownerId: d.OwnerId,
			localId: d.Order.LocalId,
			localPriority: d.Order.LocalPriority,
			before: d.Order.Before,
			after: d.Order.After
		)).ToArray());

	private string? getBaselineOwnerId(MethodBase targetMethod) {
		if (assemblyOwnerResolver is null)
			throw new InternalStateException("this RuntimeHookRegistry has already been disposed/dropped");
		Assembly? asm = targetMethod.DeclaringType?.Assembly;
		if (asm is null)
			return null;
		if (assemblyOwnerResolver.TryGetOwner(asm, out string? ownerId)) {
			InternalStateException.ThrowIfInvalidOwnerId(ownerId);
			return ownerId;
		}
		return null;
	}
}

internal sealed class ManagedHookTargetState(IRuntimeHookBackend backend, RuntimeHookTargetKey target) : IDisposable, IStrongRefDroppable {
	private readonly IRuntimeHookBackend backend = backend;
	private readonly RuntimeHookTargetKey target = target;
	private IInstalledRuntimeHook[] handles = Array.Empty<IInstalledRuntimeHook>();
	private int disposed = 0;

	public void Replace(IReadOnlyList<ManagedHookDeclaration> decls) {
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("this ManagedHookTargetState has already been disposed/dropped");
		Clear();
		List<IInstalledRuntimeHook> next = new(decls.Count);
		try {
			foreach (ManagedHookDeclaration declaration in decls) {
				next.Add(
					backend.InstallManagedHook(
						new ManagedHookInstallRequest {
							TargetMethod = target.Method,
							HookMethod = declaration.HookMethod,
							OwnerId = declaration.OwnerId,
							LocalId = declaration.Order.LocalId,
						}
					)
				);
			}
			handles = next.ToArray();
		} catch {
			foreach (IInstalledRuntimeHook handle in next)
				handle.Dispose();
			throw;
		}
	}

	public void Clear() {
		IInstalledRuntimeHook[] h = Interlocked.Exchange(ref handles, Array.Empty<IInstalledRuntimeHook>());
		List<Exception>? failures = null;
		foreach (IInstalledRuntimeHook handle in h) {
			try {
				handle.Dispose();
				handle.DropStrongReferences();
			} catch (Exception ex) {
				(failures ??= new List<Exception>()).Add(ex);
			}
		}
		if (failures is not null)
			throw new AggregateException("one or more managed hook handles failed to dispose", failures);
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		Clear();
	}

	public void DropStrongReferences() {
		IInstalledRuntimeHook[] h = Interlocked.Exchange(ref handles, Array.Empty<IInstalledRuntimeHook>());
		foreach (IInstalledRuntimeHook handle in h) {
			try { handle?.Dispose(); } catch {}
			try { handle?.DropStrongReferences(); } catch {}
		}
	}
}

internal sealed class IlHookTargetState(IRuntimeHookBackend backend, RuntimeHookTargetKey target, string? baselineOwnerId) : IDisposable, IStrongRefDroppable {
	private readonly IRuntimeHookBackend backend = backend;
	private readonly RuntimeHookTargetKey target = target;
	private readonly string? baselineOwnerId = baselineOwnerId;
	private IlManipulatorRegistration[] snapshot = Array.Empty<IlManipulatorRegistration>();
	private IInstalledRuntimeHook? handle;
	private int disposed = 0;

	public void Replace(IReadOnlyList<IlHookDeclaration> decls) {
		Clear();
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("this ManagedHookTargetState has already been disposed/dropped");
		snapshot = decls.Select(static d => d.Registration).ToArray();
		if (snapshot.Length == 0)
			return;

		handle = backend.InstallIlHookPipeline(
			new IlHookPipelineInstallRequest {
				TargetMethod = target.Method,
				BaselineOwnerId = baselineOwnerId,
				GetSnapshot = ReadSnapshot,
			}
		);
	}

	public IReadOnlyList<IlManipulatorRegistration> ReadSnapshot() => Volatile.Read(ref snapshot);

	public void Clear() {
		IInstalledRuntimeHook? h = Interlocked.Exchange(ref handle, null);
		h?.Dispose();
		h?.DropStrongReferences();
		snapshot = Array.Empty<IlManipulatorRegistration>();
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		Clear();
	}

	public void DropStrongReferences() {
		IInstalledRuntimeHook? h = Interlocked.Exchange(ref handle, null);
		try { h?.Dispose(); } catch {}
		try { h?.DropStrongReferences(); } catch {}
		snapshot = Array.Empty<IlManipulatorRegistration>();
	}
}
