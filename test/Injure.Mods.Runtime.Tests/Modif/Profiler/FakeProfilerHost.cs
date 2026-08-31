// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Tests.Modif.Profiler;

/// <summary>
/// Fake stand-in for the CLR profiler.
/// </summary>
/// <remarks>
/// <para>
/// Modules are real assemblies read from disk, so baseline IL and metadata are exactly what the
/// profiler would hand over. Only the parts that need a live runtime are simulated: token
/// definition, prepared body storage, and ReJIT requests.
/// </para>
/// <para>
/// Strict where the real profiler is strict. An unknown module, a method with no body, or a
/// prepared body for a module that has unloaded are all rejected, because a permissive fake would
/// let the runtime develop habits the profiler will not tolerate.
/// </para>
/// </remarks>
internal sealed class FakeProfilerHost : IProfilerHost, IProfilerEvents, IDisposable {
	private sealed class FakeModule : IDisposable {
		private readonly FileStream stream;

		public ModuleInfo Info { get; }
		public PEReader PEReader { get; }
		public MetadataReader Metadata { get; }
		public FakeMetadataEmitter Emitter { get; }

		public FakeModule(ModuleId id, string path, bool isCollectible) {
			stream = File.OpenRead(path);
			PEReader = new PEReader(stream);
			Metadata = PEReader.GetMetadataReader();
			Guid mvid = Metadata.GetGuid(Metadata.GetModuleDefinition().Mvid);
			Info = new ModuleInfo(id, path, mvid, isCollectible);
			Emitter = new FakeMetadataEmitter(Metadata);
		}

		public byte[] ReadMethodBodyBytes(int rva, int size) {
			PEMemoryBlock block = PEReader.GetSectionData(rva);
			return block.GetReader(0, size).ReadBytes(size);
		}

		public void Dispose() {
			PEReader.Dispose();
			stream.Dispose();
		}
	}

	private readonly Dictionary<ModuleId, FakeModule> modules = new();
	private readonly Dictionary<MethodIdentity, byte[]> preparedBodies = new();
	private ulong nextModuleId = 1;

	/// <summary>
	/// Every batch passed to <see cref="RequestReJit"/>, in order.
	/// </summary>
	public List<ImmutableArray<MethodIdentity>> ReJitRequests { get; } = new();

	/// <summary>
	/// Every batch passed to <see cref="RequestRevert"/>, in order.
	/// </summary>
	public List<ImmutableArray<MethodIdentity>> RevertRequests { get; } = new();

	/// <summary>
	/// Amount of <see cref="GetBaselineIl(MethodIdentity)"/> calls that have occurred.
	public int BaselineIlReads { get; private set; }

	public event Action? ProfilerReady;
	public event Action<ModuleInfo>? ModuleLoaded;
	public event Action<ModuleId>? ModuleUnloading;
	public event Action<MethodIdentity, string>? ReJitFailed;

	public void Dispose() {
		foreach (FakeModule module in modules.Values)
			module.Dispose();
		modules.Clear();
	}

	// ==========================================================================================
	// using the fake

	/// <summary>
	/// Loads an assembly from disk as a module and raises <see cref="ModuleLoaded"/>.
	/// </summary>
	public ModuleInfo LoadModule(string path, bool isCollectible = false) {
		FakeModule module = new(new ModuleId(nextModuleId++), path, isCollectible);
		modules[module.Info.Id] = module;
		ModuleLoaded?.Invoke(module.Info);
		return module.Info;
	}

	public void RaiseProfilerReady() => ProfilerReady?.Invoke();

	public void UnloadModule(ModuleId id) {
		ModuleUnloading?.Invoke(id);
		if (modules.Remove(id, out FakeModule? module))
			module.Dispose();
		foreach (MethodIdentity method in preparedBodies.Keys.Where(m => m.Module == id).ToArray())
			preparedBodies.Remove(method);
	}

	public void RaiseReJitFailed(MethodIdentity method, string message) => ReJitFailed?.Invoke(method, message);

	/// <summary>
	/// The bytes most recently stored for a method, or <see langword="null"/> if none were.
	/// </summary>
	public byte[]? GetPreparedBody(MethodIdentity method) =>
		preparedBodies.TryGetValue(method, out byte[]? body) ? body : null;

	/// <summary>
	/// Every method with a stored body.
	/// </summary>
	public IReadOnlyCollection<MethodIdentity> PreparedMethods => preparedBodies.Keys;

	public FakeMetadataEmitter GetEmitter(ModuleId module) => get(module).Emitter;

	/// <summary>
	/// Finds a method by declaring type name and method name.
	/// </summary>
	public MethodIdentity FindMethod(ModuleId module, string declaringTypeName, string methodName) {
		MetadataReader metadata = get(module).Metadata;
		foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions) {
			MethodDefinition method = metadata.GetMethodDefinition(handle);
			if (metadata.GetString(method.Name) != methodName)
				continue;
			TypeDefinition owner = metadata.GetTypeDefinition(method.GetDeclaringType());
			if (metadata.GetString(owner.Name) == declaringTypeName)
				return new MethodIdentity(module, MetadataTokens.GetToken(handle));
		}
		throw new InvalidOperationException($"{declaringTypeName}::{methodName} not found in {module}");
	}

	// ==========================================================================================
	// IProfilerHost
	public bool IsAttached { get; set; } = true;

	public ImmutableArray<ModuleInfo> GetLoadedModules() => modules.Values.Select(static m => m.Info).ToImmutableArray();

	public ImmutableArray<byte> GetBaselineIl(MethodIdentity method) {
		BaselineIlReads++;
		FakeModule module = get(method.Module);
		var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MethodDefToken);
		MethodDefinition definition = module.Metadata.GetMethodDefinition(handle);
		if (definition.RelativeVirtualAddress == 0)
			throw new InvalidOperationException($"{method} has no IL body");

		MethodBodyBlock body = module.PEReader.GetMethodBody(definition.RelativeVirtualAddress);
		return module.ReadMethodBodyBytes(definition.RelativeVirtualAddress, body.Size).ToImmutableArray();
	}

	public MetadataReader GetMetadata(ModuleId module) => get(module).Metadata;

	public IMetadataEmitter GetMetadataEmitter(ModuleId module) => get(module).Emitter;

	public void SetPreparedBody(MethodIdentity method, ReadOnlySpan<byte> body) {
		get(method.Module);
		preparedBodies[method] = body.ToArray();
	}

	public void RequestReJit(ReadOnlySpan<MethodIdentity> methods) {
		foreach (MethodIdentity method in methods) {
			get(method.Module);
			if (!preparedBodies.ContainsKey(method))
				throw new InvalidOperationException($"{method} was requested for ReJIT with no prepared body");
		}
		ReJitRequests.Add(methods.ToImmutableArray());
	}

	public void RequestRevert(ReadOnlySpan<MethodIdentity> methods) {
		foreach (MethodIdentity method in methods) {
			get(method.Module);
			preparedBodies.Remove(method);
		}
		RevertRequests.Add(methods.ToImmutableArray());
	}

	private FakeModule get(ModuleId id) =>
		modules.TryGetValue(id, out FakeModule? module) ? module : throw new InvalidOperationException($"{id} is not loaded");
}

internal sealed class FakeMetadataEmitter : IMetadataEmitter {
	private readonly Dictionary<TableIndex, int> nextRowId = [];
	private readonly Dictionary<string, int> dedup = new(StringComparer.Ordinal);

	public List<(TableIndex Table, int Token)> Defined { get; } = [];

	/// <summary>
	/// How many times <see cref="Commit"/> was called with pending rows.
	/// </summary>
	public int Commits { get; private set; }

	public FakeMetadataEmitter(MetadataReader metadata) {
		ArgumentNullException.ThrowIfNull(metadata);
		foreach (TableIndex table in (TableIndex[])[
			TableIndex.AssemblyRef, TableIndex.TypeRef, TableIndex.TypeSpec,
			TableIndex.MemberRef, TableIndex.MethodSpec, TableIndex.StandAloneSig,
		])
			nextRowId[table] = metadata.GetTableRowCount(table) + 1;
	}

	public int CountDefined(TableIndex table) => Defined.Count(d => d.Table == table);

	public int DefineAssemblyReference(IlAssemblyIdentity identity) =>
		allocate(TableIndex.AssemblyRef, $"asm:{identity}");

	public int DefineTypeReference(int resolutionScope, string @namespace, string name) =>
		allocate(TableIndex.TypeRef, $"tr:{resolutionScope:x8}:{@namespace}:{name}");

	public int DefineTypeSpecification(ReadOnlySpan<byte> signature) =>
		allocate(TableIndex.TypeSpec, $"ts:{Convert.ToHexString(signature)}");

	public int DefineMemberReference(int parent, string name, ReadOnlySpan<byte> signature) =>
		allocate(TableIndex.MemberRef, $"mr:{parent:x8}:{name}:{Convert.ToHexString(signature)}");

	public int DefineMethodSpecification(int method, ReadOnlySpan<byte> signature) =>
		allocate(TableIndex.MethodSpec, $"ms:{method:x8}:{Convert.ToHexString(signature)}");

	public int DefineStandaloneSignature(ReadOnlySpan<byte> signature) =>
		allocate(TableIndex.StandAloneSig, $"sig:{Convert.ToHexString(signature)}");

	public int DefineUserString(string value) {
		if (dedup.TryGetValue($"us:{value}", out int existing))
			return existing;
		int token = 0x70000000 | (dedup.Count + 1);
		dedup[$"us:{value}"] = token;
		return token;
	}

	public void Commit() => Commits++;

	private int allocate(TableIndex table, string key) {
		if (dedup.TryGetValue(key, out int existing))
			return existing;
		int rowId = nextRowId[table]++;
		int token = ((int)table << 24) | rowId;
		dedup[key] = token;
		Defined.Add((table, token));
		return token;
	}
}
