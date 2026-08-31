// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Runtime.Modif;
using Injure.Mods.Runtime.Modif.Detours;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Tests.Modif.E2e;

[ModLifetimeIdentityBelongsTo("e2e")]
internal readonly struct E2eL : IModLifetimeIdentity;

public sealed class E2eFixture : IDisposable {
	private readonly ConcurrentDictionary<Guid, ModuleId> moduleByMvid = new();
	private readonly ConcurrentDictionary<ModuleId, Module> reflectionModuleById = new();

	internal ProfilerHost Host { get; }
	// make every owner compare equal so ordering falls back to registration sequence
	// none of the e2e tests currently need genuine correct by-name ordering so this happens to work
	internal ModifRegistry Registry { get; } = new(Comparer<string>.Create((_, _) => 0));
	internal MethodTransformCache Cache { get; } = new();
	internal DetourTransform Detours { get; }
	internal ModifOrchestrator Orchestrator { get; }

	public E2eFixture() {
		if (Native.prof_is_attached() == 0)
			throw new InvalidOperationException("the profiler is not attached; these tests must be launched through run-with-profiler.sh");

		Host = new ProfilerHost();
		foreach (ModuleInfo info in Host.GetLoadedModules())
			moduleByMvid[info.Mvid] = info.Id;
		Host.ModuleLoaded += info => moduleByMvid[info.Mvid] = info.Id;

		Detours = new DetourTransform(resolveMethod);
		Orchestrator = new ModifOrchestrator(Host, Registry, Cache, default, null, Detours);
		Orchestrator.Attach(Host);
	}

	public void Dispose() {
		Orchestrator.Dispose();
		Host.Dispose();
	}

	internal MethodIdentity GetIdentity(MethodBase method) {
		Module module = method.Module;
		Guid mvid = module.ModuleVersionId;
		ModuleId moduleId = waitFor(moduleByMvid, mvid, TimeSpan.FromSeconds(2))
			?? throw new TimeoutException($"module '{module.Name}' (mvid {mvid}) never got reported as loaded");
		reflectionModuleById.TryAdd(moduleId, module);
		return new MethodIdentity(moduleId, method.MetadataToken);
	}

	private static ModuleId? waitFor(
		ConcurrentDictionary<Guid, ModuleId> moduleByMvid,
		Guid mvid,
		TimeSpan timeout
	) {
		var sw = Stopwatch.StartNew();
		while (sw.Elapsed < timeout) {
			if (moduleByMvid.TryGetValue(mvid, out ModuleId module))
				return module;
			Thread.Sleep(10);
		}
		sw.Stop();
		return moduleByMvid.TryGetValue(mvid, out ModuleId last) ? last : null;
	}

	private MethodBase resolveMethod(MethodIdentity identity) {
		if (!reflectionModuleById.TryGetValue(identity.Module, out Module? module))
			throw new InvalidOperationException(
				$"{identity} was never looked up through {nameof(GetIdentity)}, so its reflection Module is unknown"
			);
		return module.ResolveMethod(identity.MethodDefToken)
			?? throw new InvalidOperationException($"{identity} does not resolve to a method");
	}
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2eCollection : ICollectionFixture<E2eFixture> {
	public const string Name = "e2e profiler testss";
}
