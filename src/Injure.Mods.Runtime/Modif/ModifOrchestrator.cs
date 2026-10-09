// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;
using Injure.Mods.Runtime.Modif.Detours;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Modif;

internal sealed class ApplyResult(
	ImmutableArray<MethodIdentity> applied,
	ImmutableArray<MethodIdentity> reverted,
	int upToDate
) {
	public ImmutableArray<MethodIdentity> Applied { get; } = applied;
	public ImmutableArray<MethodIdentity> Reverted { get; } = reverted;
	public int UpToDate { get; } = upToDate;
}

internal sealed class ModifOrchestrator : IDisposable {
	private sealed record ModuleDecoding(
		MetadataReader Metadata,
		SrmReferenceDecoder Decoder,
		ProfilerTokenResolver Resolver
	);

	private readonly IProfilerHost prof;
	private readonly ModifRegistry registry;
	private readonly MethodTransformCache cache;
	private readonly IIlOwnerInfoProvider? ownerInfoProvider;
	private readonly IIlCallDispatch? callDispatch;
	private readonly IDetourTransform detours;
	private readonly IModuleOwnerResolver? moduleOwnerResolver;
	private readonly Lock @lock = new();
	private readonly Dictionary<ModuleId, ModuleDecoding> modules = new();
	private readonly HashSet<MethodIdentity> installed = new();
	private byte[] scratch = new byte[512];

	public ModifOrchestrator(
		IProfilerHost prof,
		ModifRegistry registry,
		MethodTransformCache cache,
		IIlOwnerInfoProvider? ownerInfoProvider,
		IIlCallDispatch? callDispatch,
		IDetourTransform detours,
		IModuleOwnerResolver? moduleOwnerResolver
	) {
		InternalStateException.ThrowIfNull(prof);
		InternalStateException.ThrowIfNull(registry);
		InternalStateException.ThrowIfNull(cache);
		InternalStateException.ThrowIfNull(detours);
		this.prof = prof;
		this.registry = registry;
		this.cache = cache;
		this.ownerInfoProvider = ownerInfoProvider;
		this.callDispatch = callDispatch;
		this.detours = detours;
		this.moduleOwnerResolver = moduleOwnerResolver;
	}

	public void Attach(IProfilerEvents events) {
		ArgumentNullException.ThrowIfNull(events);
		events.ModuleUnloading += ForgetModule;
	}

	public void Dispose() {
		lock (@lock) {
			modules.Clear();
			installed.Clear();
		}
	}

	public void ForgetModule(ModuleId module) {
		lock (@lock) {
			foreach (MethodIdentity method in registry.ModifiedMethods)
				if (method.Module == module && detours.HasChain(method))
					detours.UpdateChain(method, []);
			registry.RemoveModule(module);
			cache.EvictModule(module);
			modules.Remove(module);
			installed.RemoveWhere(m => m.Module == module);
		}
	}

	public ApplyResult ApplyPending() => Apply(registry.DrainDirty());

	/// <remarks>
	/// A method already prepared at its current generation is counted as up to date and skipped, so
	/// calling this redundantly costs only a cache lookup rather than a pipeline run.
	/// </remarks>
	public ApplyResult Apply(ImmutableArray<MethodIdentity> methods) {
		if (methods.IsDefaultOrEmpty)
			return new ApplyResult([], [], 0);

		lock (@lock) {
			List<MethodIdentity> reverted = new();
			List<MethodIdentity> prepared = new();
			HashSet<ModuleId> touchedModules = new();
			int upToDate = 0;

			// these intentionally survive an abort
			foreach (MethodIdentity method in methods)
				if (!registry.GetGeneration(method).IsModified && revert(method))
					reverted.Add(method);

			MethodIdentity current = methods[0];
			try {
				foreach (MethodIdentity method in methods) {
					current = method;
					MethodGeneration generation = registry.GetGeneration(method);
					if (!generation.IsModified)
						continue;

					if (cache.TryGetEncoded(method, generation, out _) && installed.Contains(method)) {
						upToDate++;
						continue;
					}

					prepare(method, generation);
					touchedModules.Add(method.Module);
					prepared.Add(method);
				}
			} catch (Exception ex) {
				// run commit here too; the resolver already cached tokens it handed out during encoding so
				// not committing would make a later pass emit il that names rows that were never applied
				// also, rows already defined intentionally stay defined, it's harmless and makes a retry cheaper
				commit(touchedModules);
				registry.MarkDirty(methods);
				throw new ModifException(current, ex);
			}

			commit(touchedModules);

			if (prepared.Count > 0) {
				prof.RequestReJit(prepared.ToArray());
				foreach (MethodIdentity method in prepared)
					installed.Add(method);
			}

			return new ApplyResult(prepared.ToImmutableArray(), reverted.ToImmutableArray(), upToDate);
		}
	}

	/// <summary>
	/// Refreshes a method's dispatch chain without re-transforming or ReJIT-ing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Basically only the part of <c>prepare()</c> (private method in this class) that resyncs
	/// the chain. <see cref="ModifRegistry.AddDetour(MethodIdentity, OwnerOrderedEntry{DetourRegistration})"/>
	/// (intentionally) only dirties the method when its detour count goes from 0 to 1, so
	/// registering a 2nd+ detour on a method needs another means of syncing the chain, which is
	/// exactly what this does.
	/// </para>
	/// <para>
	/// Detour registration should call this immediately after
	/// <see cref="ModifRegistry.AddDetour(MethodIdentity, OwnerOrderedEntry{DetourRegistration})"/>. Most tests
	/// currently don't call it purely because they happen to not need it. No-op if the method
	/// has no detour prologue yet, so over-calling doesn't incur an extra cost.
	/// </para>
	/// </remarks>
	public void SyncDetourChain(MethodIdentity method) {
		MethodGeneration generation = registry.GetGeneration(method);
		if (generation.HasDetourPrologue)
			detours.UpdateChain(method, registry.GetDetours(method));
	}

	private void prepare(MethodIdentity method, MethodGeneration generation) {
		ModuleDecoding dec = decodingFor(method.Module);

		if (!cache.TryGetBaseline(method, out IlMethodBody baseline)) {
			baseline = decode(dec, method);
			cache.SetBaseline(method, baseline);
		}

		if (!cache.TryGetTransformed(method, generation.Patch, out IlMethodBody transformed)) {
			ImmutableArray<IlManipulatorRegistration> manipulators = registry.GetManipulators(method);
			transformed = IlPipeline.Transform(baseline, manipulators, ownerInfoProvider, callDispatch).Body;
			cache.SetTransformed(method, generation.Patch, transformed);
		}

		IlMethodBody final = transformed;
		if (generation.HasDetourPrologue) {
			ImmutableArray<DetourRegistration> curr = registry.GetDetours(method);
			detours.UpdateChain(method, curr);
			final = detours.Apply(method, transformed.Clone(), curr);
		} else if (detours.HasChain(method)) {
			detours.UpdateChain(method, []);
		}

		IlEncodedMethodBody encoded = SrmMethodBodyEncoder.Prepare(final, dec.Resolver);
		cache.SetEncoded(method, generation, encoded);
		prof.SetPreparedBody(method, write(encoded));
	}

	private bool revert(MethodIdentity method) {
		if (detours.HasChain(method))
			detours.UpdateChain(method, []);
		cache.EvictDerived(method);
		if (!installed.Remove(method))
			return false;
		prof.RequestRevert([method]);
		return true;
	}

	private void commit(HashSet<ModuleId> touchedModules) {
		foreach (ModuleId module in touchedModules)
			if (modules.TryGetValue(module, out ModuleDecoding? dec))
				dec.Resolver.Commit();
		touchedModules.Clear();
	}

	private IlMethodBody decode(ModuleDecoding dec, MethodIdentity method) {
		ImmutableArray<byte> il = prof.GetBaselineIl(method);
		var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MethodDefToken);
		InternalIlProvenance baseline = (moduleOwnerResolver?.TryGetOwner(method.Module, out string? ownerId) ?? false)
			? new InternalIlProvenance(ownerId, null)
			: default;
		unsafe {
			fixed (byte* p = il.AsSpan())
				return SrmMethodBodyDecoder.Decode(dec.Metadata, handle, new BlobReader(p, il.Length), baseline);
		}
	}

	private ReadOnlySpan<byte> write(IlEncodedMethodBody encoded) {
		if (scratch.Length < encoded.Size)
			scratch = new byte[Math.Max(encoded.Size, scratch.Length * 2)];
		encoded.WriteTo(scratch);
		return scratch.AsSpan(0, encoded.Size);
	}

	private ModuleDecoding decodingFor(ModuleId module) {
		if (modules.TryGetValue(module, out ModuleDecoding? existing))
			return existing;

		MetadataReader metadata = prof.GetMetadata(module);
		SrmReferenceDecoder decoder = new(metadata);
		ModuleDecoding dec = new(
			metadata,
			decoder,
			new ProfilerTokenResolver(metadata, prof.GetMetadataEmitter(module), decoder)
		);
		modules[module] = dec;
		return dec;
	}
}
