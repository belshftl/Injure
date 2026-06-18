// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Runtime;

internal readonly record struct NativeLibraryResolution(
	string ProviderOwnerID,
	string LibraryID,
	string RuntimeIdentifier,
	string FullPath
);

internal enum NativeImportPhase {
	Load,
	LinkOrLater,
}

internal sealed class NativeLibraryRegistry(string currentRid) {
	private sealed class ImporterInfo {
		public required ReloadGeneration Generation;
	}

	private readonly Dictionary<(string OwnerID, string ID), NativeLibraryResolution> libraries = new();
	private readonly Dictionary<string, IntPtr> loadedByPath = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
	private readonly ConditionalWeakTable<Assembly, ImporterInfo> importers = new();
	private readonly Dictionary<ReloadGeneration, NativeImportPhase> phaseByGeneration = new();
	private readonly Dictionary<string, HashSet<string>> dependenciesByOwner = new(StringComparer.Ordinal);
	private readonly string currentRid = currentRid;

	public void Rebuild(IReadOnlyList<StagedMod> staged, ResolvedModGraph graph) {
		libraries.Clear();
		dependenciesByOwner.Clear();

		foreach (ResolvedMod mod in graph.Mods.Values) {
			HashSet<string> deps = new(StringComparer.Ordinal);
			foreach (ModRelationshipManifest rel in mod.Manifest.Relationships)
				deps.Add(rel.OwnerID);
			dependenciesByOwner[mod.Manifest.OwnerID] = deps;
		}

		foreach (StagedMod mod in staged) {
			if (mod.Manifest.Reloadable && mod.Manifest.NativeLibraries.Count != 0)
				throw new InternalStateException("reloadable mod has declared native libraries; this should've been caught in the manifest reader");
			foreach (ModNativeLibraryManifest lib in mod.Manifest.NativeLibraries) {
				if (lib.RuntimeIdentifier != currentRid)
					continue;

				string path = Path.GetFullPath(Path.Combine(mod.StagedRoot, lib.Path));
				if (!File.Exists(path))
					throw new ModLoadException(mod.Manifest.OwnerID, $"native library '{lib.Path}' not found");

				if (!libraries.TryAdd((mod.Manifest.OwnerID, lib.ID), new NativeLibraryResolution(mod.Manifest.OwnerID, lib.ID, lib.RuntimeIdentifier, path)))
					throw new ModLoadException(mod.Manifest.OwnerID, $"duplicate native library '{mod.Manifest.OwnerID}::{lib.ID}' for RID '{currentRid}'");
			}
		}
	}

	public void RegisterAssembly(Assembly assembly, ReloadGeneration generation) {
		importers.Remove(assembly);
		importers.Add(assembly, new ImporterInfo { Generation = generation });
		phaseByGeneration.TryAdd(generation, NativeImportPhase.Load);
		NativeLibrary.SetDllImportResolver(assembly, Resolve); // TODO maybe catch and error on InvalidOperationException
	}

	public void SetPhase(ReloadGeneration generation, NativeImportPhase phase) => phaseByGeneration[generation] = phase;
	public void UnregisterGeneration(ReloadGeneration generation) {
		try { phaseByGeneration.Remove(generation); } catch {}
	}

	public IntPtr Resolve(string libraryName, Assembly importingAssembly, DllImportSearchPath? searchPath) {
		int i = libraryName.IndexOf("::", StringComparison.Ordinal);
		if (i < 0)
			return IntPtr.Zero;
		if (libraryName.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
			throw new InvalidOperationException("mod native library name must contain exactly one occurrence of ::");
		string provider = libraryName[..i];
		if (!ModMetadataValidation.ValidateOwnerID(provider, out string? err))
			throw new InvalidOperationException($"invalid mod native library provider ID: {err}");
		string id = libraryName[(i + 2)..];
		if (!ModMetadataValidation.ValidateLocalID(id, out err))
			throw new InvalidOperationException($"invalid mod native library ID: {err}");

		if (!importers.TryGetValue(importingAssembly, out ImporterInfo? ii))
			return IntPtr.Zero;
		string importer = ii.Generation.OwnerID;
		if (importer != provider) {
			if (!phaseByGeneration.TryGetValue(ii.Generation, out NativeImportPhase phase) || phase != NativeImportPhase.LinkOrLater)
				throw new InvalidOperationException($"mod '{importer}' cannot use native library '{provider}::{id}' before LinkAsync");
			if (!dependenciesByOwner.TryGetValue(importer, out HashSet<string>? deps) || !deps.Contains(provider))
				throw new InvalidOperationException($"mod '{importer}' cannot use native library '{provider}::{id}' without declaring a dependency on '{provider}'");
		}
		if (!libraries.TryGetValue((provider, id), out NativeLibraryResolution lib))
			throw new DllNotFoundException($"native library '{provider}::{id}' doesn't have a version for this platform ('{currentRid}')");
		lock (loadedByPath) {
			if (loadedByPath.TryGetValue(lib.FullPath, out IntPtr existing))
				return existing;
			IntPtr handle = NativeLibrary.Load(lib.FullPath, importingAssembly, searchPath);
			loadedByPath.Add(lib.FullPath, handle);
			return handle;
		}
	}
}
