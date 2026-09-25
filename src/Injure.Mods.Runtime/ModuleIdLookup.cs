// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Injure.Collections;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime;

internal sealed class ModuleIdLookup : IDisposable {
	private readonly IProfilerEvents events;
	private readonly Dictionary<ModuleId, ModuleInfo> unbound = new(); // seen by the profiler, no Module bound yet
	private readonly BijectiveMap<Module, ModuleId> bound = new();
	private readonly Lock @lock = new();

	public ModuleIdLookup(IProfilerHost prof, IProfilerEvents events) {
		InternalStateException.ThrowIfNull(prof);
		InternalStateException.ThrowIfNull(events);
		this.events = events;

		// subscribe first, since snapshotting first would have a race window between the snapshot and
		// the subscriptions, whereas by subscribing first the bug turns from "potentially miss a module"
		// to "potentially record a module twice", which is harmless (see `recordLocked` below)
		events.ModuleLoaded += onModuleLoaded;
		events.ModuleUnloading += onModuleUnloading;
		ImmutableArray<ModuleInfo> modules = prof.GetLoadedModules();
		lock (@lock)
			foreach (ModuleInfo info in modules)
				onModuleLoaded(info);
	}

	public void Dispose() {
		events.ModuleLoaded -= onModuleLoaded;
		events.ModuleUnloading -= onModuleUnloading;
	}

	public bool TryGetModuleId(Module module, out ModuleId moduleId) {
		InternalStateException.ThrowIfNull(module);
		lock (@lock) {
			if (bound.TryGetByLeft(module, out moduleId))
				return true;
			foreach ((ModuleId id, ModuleInfo info) in unbound) {
				if (matches(info, module)) {
					unbound.Remove(id);
					bound.TryAdd(module, id);
					moduleId = id;
					return true;
				}
			}
			return false;
		}
	}

	public bool TryGetModule(ModuleId moduleId, [NotNullWhen(true)] out Module? module) {
		lock (@lock) {
			if (bound.TryGetByRight(moduleId, out module))
				return true;
			if (!unbound.TryGetValue(moduleId, out ModuleInfo info))
				return false;
			foreach (AssemblyLoadContext alc in AssemblyLoadContext.All) {
				foreach (Assembly asm in alc.Assemblies) {
					if (matches(info, asm.ManifestModule) && !bound.ContainsLeft(asm.ManifestModule)) {
						unbound.Remove(moduleId);
						bound.TryAdd(asm.ManifestModule, moduleId);
						module = asm.ManifestModule;
						return true;
					}
				}
			}
			module = null;
			return false;
		}
	}

	public void ForgetAssembly(Assembly asm) {
		lock (@lock)
			foreach (Module module in asm.Modules)
				bound.RemoveByLeft(module);
	}

	private void onModuleLoaded(ModuleInfo info) {
		lock (@lock)
			if (!bound.ContainsRight(info.Id))
				unbound[info.Id] = info;
	}

	private void onModuleUnloading(ModuleId id) {
		lock (@lock) {
			unbound.Remove(id);
			bound.RemoveByRight(id);
		}
	}

	// staged paths are unique per generation so this disambiguates same-mvid generations
	private static bool matches(ModuleInfo info, Module module) =>
		module.ModuleVersionId == info.Mvid &&
		string.Equals(Path.GetFullPath(module.Assembly.Location), Path.GetFullPath(info.Path), StringComparison.OrdinalIgnoreCase);
}
