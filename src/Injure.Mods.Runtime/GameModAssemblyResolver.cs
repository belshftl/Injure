// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Injure.Mods.Runtime;

/// <summary>
/// Lets the engine's/game's load contexts resolve references to non-reloadable mods, since
/// <c>AssemblyRef</c>s to those can be emitted by patches.
/// </summary>
internal sealed class GameModAssemblyResolver : IDisposable {
	private readonly AssemblyLoadContext[] hosts;
	private StrongBox<ImmutableArray<AssemblyLoadContext>> modAlcs = new([]);

	/// <param name="hostAssemblies">
	/// Assemblies whose methods can be patched to reference a mod, i.e. the engine's and the game's.
	/// </param>
	public GameModAssemblyResolver(IEnumerable<Assembly> hostAssemblies) {
		hosts = hostAssemblies
			.Select(static a => AssemblyLoadContext.GetLoadContext(a) ?? AssemblyLoadContext.Default)
			.Distinct()
			.ToArray();
		foreach (AssemblyLoadContext host in hosts)
			host.Resolving += resolve;
	}

	public void Add(AssemblyLoadContext alc) {
		if (alc.IsCollectible)
			throw new InternalStateException($"'{alc.Name}' is collectible and must never be referenced from the engine/game");
		StrongBox<ImmutableArray<AssemblyLoadContext>> box = Volatile.Read(ref modAlcs);
		ImmutableArray<AssemblyLoadContext> newArr = box.Value.Add(alc);
		Volatile.Write(ref modAlcs, new StrongBox<ImmutableArray<AssemblyLoadContext>>(newArr));
	}

	public void Clear() => Volatile.Write(ref modAlcs, new StrongBox<ImmutableArray<AssemblyLoadContext>>([]));

	public void Dispose() {
		foreach (AssemblyLoadContext host in hosts)
			host.Resolving -= resolve;
		Clear();
	}

	private Assembly? resolve(AssemblyLoadContext requester, AssemblyName name) {
		// i'm pretty sure this runs on a jit thread so just read a snapshot and don't lock/etc
		foreach (AssemblyLoadContext alc in Volatile.Read(ref modAlcs).Value)
			foreach (Assembly asm in alc.Assemblies)
				if (AssemblyName.ReferenceMatchesDefinition(name, asm.GetName()))
					return asm;
		return null;
	}
}
