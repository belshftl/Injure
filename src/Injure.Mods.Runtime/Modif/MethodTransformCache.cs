// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Modif;

/// <remarks>
/// <para>
/// Cached bodies are shared, not copied. Anything you get back from this is readonly, and
/// <see cref="IlMethodBody.Clone"/> must be used before transforming further. The pipeline returns
/// its baseline argument unchanged when no manipulator edits anything, so a cached transformed body
/// and the cached baseline may be the same instance.
/// </para>
/// <para>
/// Thread-safe, achieved by mutexing.
/// </para>
/// </remarks>
internal sealed class MethodTransformCache {
	private sealed class Entry {
		public IlMethodBody? Baseline { get; set; }
		public IlMethodBody? Transformed { get; set; }
		public int TransformedGeneration { get; set; } = -1;
		public IlEncodedMethodBody? Encoded { get; set; }
		public MethodGeneration EncodedGeneration { get; set; } = new(-1, false);

		public void ClearDerived() {
			Transformed = null;
			TransformedGeneration = -1;
			Encoded = null;
			EncodedGeneration = new MethodGeneration(-1, false);
		}
	}

	private readonly Lock @lock = new();
	private readonly Dictionary<MethodIdentity, Entry> entries = new();

	/// <summary>
	/// Amount of methods with at least one cached artifact.
	/// </summary>
	public int Count {
		get {
			lock (@lock)
				return entries.Count;
		}
	}

	// ==========================================================================================
	// baseline

	/// <summary>
	/// Looks up the method's original decoded body, which never changes while its module is loaded.
	/// </summary>
	public bool TryGetBaseline(MethodIdentity method, out IlMethodBody baseline) {
		lock (@lock) {
			baseline = entries.TryGetValue(method, out Entry? entry) ? entry.Baseline! : null!;
			return baseline is not null;
		}
	}

	public void SetBaseline(MethodIdentity method, IlMethodBody baseline) {
		ArgumentNullException.ThrowIfNull(baseline);
		lock (@lock)
			entryFor(method).Baseline = baseline;
	}

	// ==========================================================================================
	// transformed

	/// <summary>
	/// Looks up the body after the manipulator pipeline, before any detour prologue.
	/// </summary>
	public bool TryGetTransformed(MethodIdentity method, int patchGeneration, out IlMethodBody transformed) {
		lock (@lock) {
			if (entries.TryGetValue(method, out Entry? entry) && entry.TransformedGeneration == patchGeneration) {
				transformed = entry.Transformed!;
				return transformed is not null;
			}
			transformed = null!;
			return false;
		}
	}

	public void SetTransformed(MethodIdentity method, int patchGeneration, IlMethodBody transformed) {
		ArgumentNullException.ThrowIfNull(transformed);
		lock (@lock) {
			Entry entry = entryFor(method);
			entry.Transformed = transformed;
			entry.TransformedGeneration = patchGeneration;
		}
	}

	// ==========================================================================================
	// encoded

	/// <summary>
	/// Looks up the bytes handed to the profiler, which depend on the manipulators and on whether
	/// a detour prologue is present.
	/// </summary>
	public bool TryGetEncoded(MethodIdentity method, MethodGeneration generation, out IlEncodedMethodBody encoded) {
		lock (@lock) {
			if (entries.TryGetValue(method, out Entry? entry) && entry.EncodedGeneration == generation) {
				encoded = entry.Encoded!;
				return encoded is not null;
			}
			encoded = null!;
			return false;
		}
	}

	public void SetEncoded(MethodIdentity method, MethodGeneration generation, IlEncodedMethodBody encoded) {
		ArgumentNullException.ThrowIfNull(encoded);
		lock (@lock) {
			Entry entry = entryFor(method);
			entry.Encoded = encoded;
			entry.EncodedGeneration = generation;
		}
	}

	// ==========================================================================================
	// eviction

	/// <summary>
	/// Drops everything derived from a method's registrations, keeping its baseline.
	/// </summary>
	/// <remarks>
	/// For methods that been reverted to their original IL, since re-decoding the baseline
	/// there would be pointless.
	/// </remarks>
	public void EvictDerived(MethodIdentity method) {
		lock (@lock) {
			if (!entries.TryGetValue(method, out Entry? entry))
				return;
			entry.ClearDerived();
			if (entry.Baseline is null)
				entries.Remove(method);
		}
	}

	/// <summary>
	/// Drops everything cached for a method.
	/// </summary>
	public void Evict(MethodIdentity method) {
		lock (@lock)
			entries.Remove(method);
	}

	/// <summary>
	/// Drops everything cached for a module.
	/// </summary>
	/// <remarks>
	/// Called on module unload, before the module's <see cref="ModuleId"/> becomes invalid and
	/// possibly reused. Every reference in a cached body belongs to metadata that is going away.
	/// </remarks>
	public void EvictModule(ModuleId module) {
		lock (@lock)
			foreach (MethodIdentity method in entries.Keys.Where(m => m.Module == module).ToArray())
				entries.Remove(method);
	}

	public void Clear() {
		lock (@lock)
			entries.Clear();
	}

	private Entry entryFor(MethodIdentity method) {
		if (!entries.TryGetValue(method, out Entry? entry)) {
			entry = new Entry();
			entries[method] = entry;
		}
		return entry;
	}
}
