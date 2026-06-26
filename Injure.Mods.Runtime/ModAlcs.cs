// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Injure.Mods.Runtime;

internal sealed class ModAlc(string entryAssemblyPath, IEnumerable<string> sharedAssemblyNames, ModContractsAlc contracts, string name) : AssemblyLoadContext(name, isCollectible: true) {
	private readonly AssemblyDependencyResolver resolver = new(entryAssemblyPath);
	private readonly HashSet<string> sharedAssemblyNames = new(sharedAssemblyNames, StringComparer.OrdinalIgnoreCase);
	private readonly ModContractsAlc contracts = contracts;

	protected override Assembly? Load(AssemblyName assemblyName) {
		ArgumentNullException.ThrowIfNull(assemblyName);
		if (assemblyName.Name is not null && sharedAssemblyNames.Contains(assemblyName.Name))
			return null;
		if (contracts.TryGet(assemblyName, out Assembly? loaded)) {
			AssemblyName loadedName = loaded.GetName();
			if (assemblyName.Version is not null && loadedName.Version is not null && assemblyName.Version != loadedName.Version)
				throw new FileLoadException($"requested shared contract assembly '{assemblyName.FullName}', but runtime loaded '{loaded.FullName}'");
			return loaded;
		}
		string? path = resolver.ResolveAssemblyToPath(assemblyName);
		return path is null ? null : LoadFromAssemblyPath(path);
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
		ArgumentNullException.ThrowIfNull(unmanagedDllName);
		string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
	}
}

internal sealed class ModContractsAlc(IReadOnlyDictionary<string, string> pathsBySimpleName) : AssemblyLoadContext("mod-contracts", isCollectible: false) {
	private readonly IReadOnlyDictionary<string, string> pathsBySimpleName = pathsBySimpleName;
	private readonly Dictionary<string, Assembly> assembliesBySimpleName = new(StringComparer.OrdinalIgnoreCase);

	protected override Assembly? Load(AssemblyName assemblyName) {
		if (assemblyName.Name is not null && assembliesBySimpleName.TryGetValue(assemblyName.Name, out Assembly? asm))
			return asm;
		if (assemblyName.Name is not null && pathsBySimpleName.TryGetValue(assemblyName.Name, out string? path)) {
			Assembly loaded = LoadFromAssemblyPath(path);
			assembliesBySimpleName.Add(assemblyName.Name, loaded);
			return loaded;
		}
		return null;
	}

	public bool TryGet(AssemblyName requested, [NotNullWhen(true)] out Assembly? asm) {
		if (Load(requested) is Assembly a) {
			asm = a;
			return true;
		}
		asm = null;
		return false;
	}
}
