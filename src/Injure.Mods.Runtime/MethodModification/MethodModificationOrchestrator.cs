// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;
using Injure.Mods.Runtime.MethodModification.Detours;
using Injure.Mods.Runtime.MethodModification.Profiler;

namespace Injure.Mods.Runtime.MethodModification;

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
internal sealed class MethodModificationException : Exception {
	/// <summary>
	/// The method that was being transformed when the pass failed.
	/// </summary>
	public MethodIdentity Method { get; }

	/// <summary>
	/// The owner responsible, or <see langword="null"/> if the failure is not attributable.
	/// </summary>
	public string? OwnerId { get; }

	/// <summary>
	/// The local ID of the modification responsible, or <see langword="null"/> if the failure
	/// is not attributable.
	/// </summary>
	public string? LocalId { get; }

	public MethodModificationException(MethodIdentity method, Exception ex) : base($"failed to transform {method}: {ex.Message}", ex) {
		Method = method;
		(OwnerId, LocalId) = ex switch {
			IlManipulatorException manipulator => (manipulator.OwnerId, manipulator.LocalId),
			IlPipelineValidationException validation => (validation.OwnerId, validation.LocalId),
			_ => (null, null),
		};
	}
}

internal sealed class ApplyResult(
	ImmutableArray<MethodIdentity> applied,
	ImmutableArray<MethodIdentity> reverted,
	int upToDate
) {
	public ImmutableArray<MethodIdentity> Applied { get; } = applied;
	public ImmutableArray<MethodIdentity> Reverted { get; } = reverted;
	public int UpToDate { get; } = upToDate;
}

internal sealed class MethodModificationOrchestrator : IDisposable {
	private sealed record ModuleContext(
		MetadataReader Metadata,
		SrmReferenceDecoder Decoder,
		ProfilerTokenResolver Resolver
	);

	private readonly IProfilerHost host;
	private readonly MethodModificationRegistry registry;
	private readonly MethodTransformCache cache;
	private readonly IlOwnerContext ownerContext;
	private readonly IIlCallDispatch? callDispatch;
	private readonly IDetourTransform detours;
	private readonly Lock @lock = new();
	private readonly Dictionary<ModuleId, ModuleContext> modules = new();
	private readonly HashSet<MethodIdentity> installed = new();
	private byte[] scratch = new byte[512];

	public MethodModificationOrchestrator(
		IProfilerHost host,
		MethodModificationRegistry registry,
		MethodTransformCache cache,
		IlOwnerContext ownerContext,
		IIlCallDispatch? callDispatch,
		IDetourTransform detours
	) {
		InternalStateException.ThrowIfNull(host);
		InternalStateException.ThrowIfNull(registry);
		InternalStateException.ThrowIfNull(cache);
		InternalStateException.ThrowIfNull(detours);
		this.host = host;
		this.registry = registry;
		this.cache = cache;
		this.ownerContext = ownerContext;
		this.callDispatch = callDispatch;
		this.detours = detours;
	}

	public void Attach(IProfilerEvents events) {
		ArgumentNullException.ThrowIfNull(events);
		events.ModuleUnloading += OnModuleUnloading;
	}

	public void Dispose() {
		lock (@lock) {
			modules.Clear();
			installed.Clear();
		}
	}

	/// <remarks>
	/// Must complete before the module's <see cref="ModuleId"/> becomes invalid. Registrations go too,
	/// since if the methods no longer exist, there's nothing to re-transform and nothing to revert.
	/// </remarks>
	public void OnModuleUnloading(ModuleId module) {
		lock (@lock) {
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
				throw new MethodModificationException(current, ex);
			}

			commit(touchedModules);

			if (prepared.Count > 0) {
				host.RequestReJit(prepared.ToArray());
				foreach (MethodIdentity method in prepared)
					installed.Add(method);
			}

			return new ApplyResult(prepared.ToImmutableArray(), reverted.ToImmutableArray(), upToDate);
		}
	}

	private void prepare(MethodIdentity method, MethodGeneration generation) {
		ModuleContext ctx = contextFor(method.Module);

		if (!cache.TryGetBaseline(method, out IlMethodBody baseline)) {
			baseline = decode(ctx, method);
			cache.SetBaseline(method, baseline);
		}

		if (!cache.TryGetTransformed(method, generation.Patch, out IlMethodBody transformed)) {
			ImmutableArray<IlManipulatorRegistration> manipulators = registry.GetManipulators(method);
			transformed = IlPipeline.Transform(baseline, manipulators, ownerContext, callDispatch).Body;
			cache.SetTransformed(method, generation.Patch, transformed);
		}

		IlMethodBody final = generation.HasDetourPrologue
			? detours.Apply(method, transformed.Clone(), registry.GetDetours(method))
			: transformed;

		IlEncodedMethodBody encoded = SrmMethodBodyEncoder.Prepare(final, ctx.Resolver);
		cache.SetEncoded(method, generation, encoded);
		host.SetPreparedBody(method, write(encoded));
	}

	private bool revert(MethodIdentity method) {
		cache.EvictDerived(method);
		if (!installed.Remove(method))
			return false;
		host.RequestRevert([method]);
		return true;
	}

	private void commit(HashSet<ModuleId> touchedModules) {
		foreach (ModuleId module in touchedModules)
			if (modules.TryGetValue(module, out ModuleContext? context))
				context.Resolver.Commit();
		touchedModules.Clear();
	}

	private IlMethodBody decode(ModuleContext context, MethodIdentity method) {
		ImmutableArray<byte> il = host.GetBaselineIl(method);
		var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MethodDefToken);
		unsafe {
			fixed (byte* p = il.AsSpan())
				return SrmMethodBodyDecoder.Decode(context.Metadata, handle, new BlobReader(p, il.Length), default);
		}
	}

	private ReadOnlySpan<byte> write(IlEncodedMethodBody encoded) {
		if (scratch.Length < encoded.Size)
			scratch = new byte[Math.Max(encoded.Size, scratch.Length * 2)];
		encoded.WriteTo(scratch);
		return scratch.AsSpan(0, encoded.Size);
	}

	private ModuleContext contextFor(ModuleId module) {
		if (modules.TryGetValue(module, out ModuleContext? existing))
			return existing;

		MetadataReader metadata = host.GetMetadata(module);
		SrmReferenceDecoder decoder = new(metadata);
		ModuleContext context = new(
			metadata,
			decoder,
			new ProfilerTokenResolver(metadata, host.GetMetadataEmitter(module), decoder)
		);
		modules[module] = context;
		return context;
	}
}
