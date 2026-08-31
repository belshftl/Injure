// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// Resolves backend-neutral references into tokens valid for one target module, reusing the rows
/// that module already has and defining new ones only when it is necessary.
/// </summary>
/// <remarks>
/// <para>
/// Lookups need no reference decoding. Each table's key is the data the corresponding define call
/// takes (a scope token and two strings for <c>TypeRef</c>, a parent token, a name and a blob for
/// <c>MemberRef</c>) so an index over the module's original metadata is built from raw rows
/// rather than by resolving them. <c>AssemblyRef</c> is the one exception, since matching an
/// assembly identity means comparing its parts.
/// </para>
/// <para>
/// Indexes are built per table on first use. A module's <c>MemberRef</c> table can be large and many
/// patches never resolve a member at all, so it's not worth paying the runtime cost at startup.
/// </para>
/// <para>
/// Not thread-safe.
/// </para>
/// </remarks>
internal sealed class ProfilerTokenResolver : IIlTokenResolver {
	// ==========================================================================================
	// keys
	private readonly record struct TypeRowKey(int Scope, string Namespace, string Name);

	private readonly record struct MemberRowKey(int Parent, string Name, BlobKey Signature);

	private readonly struct BlobKey(byte[] bytes) : IEquatable<BlobKey> {
		private readonly byte[] bytes = bytes;
		private readonly int hash = hashOf(bytes);

		public bool Equals(BlobKey other) => hash == other.hash && bytes.AsSpan().SequenceEqual(other.bytes);
		public override bool Equals(object? obj) => obj is BlobKey other && Equals(other);
		public override int GetHashCode() => hash;

		private static int hashOf(byte[] bytes) {
			HashCode hash = new();
			hash.AddBytes(bytes);
			return hash.ToHashCode();
		}
	}

	private sealed class TypeArrayComparer : IEqualityComparer<ImmutableArray<IlTypeRef>> {
		public static TypeArrayComparer Instance { get; } = new();

		public bool Equals(ImmutableArray<IlTypeRef> x, ImmutableArray<IlTypeRef> y) {
			if (x.Length != y.Length)
				return false;
			for (int i = 0; i < x.Length; i++)
				if (x[i] == y[i])
					return false;
			return true;
		}

		public int GetHashCode(ImmutableArray<IlTypeRef> obj) {
			HashCode hash = new();
			hash.Add(obj.Length);
			foreach (IlTypeRef type in obj)
				hash.Add(type.GetHashCode());
			return hash.ToHashCode();
		}
	}

	// ==========================================================================================
	// state
	private readonly MetadataReader metadata;
	private readonly IMetadataEmitter emitter;
	private readonly SrmReferenceDecoder decoder;
	private readonly IlModuleIdentity moduleIdentity;
	private readonly IlAssemblyIdentity? selfAssembly;

	private readonly Dictionary<IlTypeRef, EntityHandle> types = new();
	private readonly Dictionary<IlMethodRef, EntityHandle> methods = new();
	private readonly Dictionary<IlFieldRef, EntityHandle> fields = new();
	private readonly Dictionary<IlMethodSignature, StandaloneSignatureHandle> callsites = new();
	private readonly Dictionary<ImmutableArray<IlTypeRef>, StandaloneSignatureHandle> localSignatures =
		new(TypeArrayComparer.Instance);
	private readonly Dictionary<string, UserStringHandle> userStrings = new(StringComparer.Ordinal);
	private readonly Dictionary<IlAssemblyIdentity, int> assemblyReferences = new();
	private readonly Dictionary<string, int> moduleReferences = new(StringComparer.Ordinal);

	private Dictionary<TypeRowKey, int>? typeReferenceIndex;
	private Dictionary<TypeRowKey, int>? typeDefinitionIndex;
	private Dictionary<MemberRowKey, int>? memberReferenceIndex;
	private Dictionary<BlobKey, int>? typeSpecificationIndex;
	private Dictionary<BlobKey, int>? standaloneSignatureIndex;
	private Dictionary<MemberRowKey, int>? methodSpecificationIndex;
	private Dictionary<IlAssemblyIdentity, int>? assemblyReferenceIndex;
	private bool pendingDefinitions;

	/// <summary>
	/// Rows defined by this resolver. For debug/test purposes.
	/// </summary>
	public int DefinedRowCount { get; private set; }

	/// <summary>
	/// Locals signatures satisfied by an origin hint, skipping both encoding and a row.
	/// For debug/test purposes.
	/// </summary>
	public int LocalSignatureOriginHits { get; private set; }

	public ProfilerTokenResolver(MetadataReader metadata, IMetadataEmitter emitter, SrmReferenceDecoder decoder) {
		InternalStateException.ThrowIfNull(metadata);
		InternalStateException.ThrowIfNull(emitter);
		InternalStateException.ThrowIfNull(decoder);
		this.metadata = metadata;
		this.emitter = emitter;
		this.decoder = decoder;
		moduleIdentity = decoder.ModuleIdentity;
		selfAssembly = metadata.IsAssembly ? IlAssemblyIdentityFactory.FromDefinition(metadata, metadata.GetAssemblyDefinition()) : null;
	}

	/// <summary>
	/// Commits every row defined since the last commit, making the tokens that were handed out
	/// usable in IL.
	/// </summary>
	/// <remarks>
	/// Cheap and idempotent when nothing was defined, so a caller can commit once per module per
	/// transform batch without checking first.
	/// </remarks>
	public void Commit() {
		if (!pendingDefinitions)
			return;
		emitter.Commit();
		pendingDefinitions = false;
	}

	// ==========================================================================================
	// IIlTokenResolver
	public EntityHandle ResolveType(IlTypeRef type) {
		InternalStateException.ThrowIfNull(type);
		if (types.TryGetValue(type, out EntityHandle cached))
			return cached;
		return types[type] = type switch {
			IlNamedTypeRef named => resolveNamedType(named),
			IlGlobalModuleTypeRef => resolveGlobalModuleType(),
			_ => MetadataTokens.EntityHandle(
				lookupOrDefineTypeSpecification(SrmSignatureEncoder.EncodeTypeSpecification(type, this))
			),
		};
	}

	public EntityHandle ResolveMethod(IlMethodRef method) {
		InternalStateException.ThrowIfNull(method);
		if (methods.TryGetValue(method, out EntityHandle cached))
			return cached;
		if (method.IsGenericInstantiation) {
			IlMethodRef definition = new(method.DeclaringType, method.Name, method.Signature, []);
			int parent = MetadataTokens.GetToken(ResolveMethod(definition));
			byte[] signature = SrmSignatureEncoder.EncodeMethodSpecification(method.GenericArguments, this).ToArray();
			return methods[method] = MetadataTokens.EntityHandle(lookupOrDefineMethodSpecification(parent, signature));
		} else {
			int parent = MetadataTokens.GetToken(ResolveType(method.DeclaringType));
			byte[] signature = SrmSignatureEncoder.EncodeMethodSignature(method.Signature, this).ToArray();
			return methods[method] = MetadataTokens.EntityHandle(lookupOrDefineMember(parent, method.Name, signature));
		}
	}

	public EntityHandle ResolveField(IlFieldRef field) {
		InternalStateException.ThrowIfNull(field);
		if (fields.TryGetValue(field, out EntityHandle cached))
			return cached;
		int parent = MetadataTokens.GetToken(ResolveType(field.DeclaringType));
		byte[] signature = SrmSignatureEncoder.EncodeFieldSignature(field.FieldType, this).ToArray();
		return fields[field] = MetadataTokens.EntityHandle(lookupOrDefineMember(parent, field.Name, signature));
	}

	public StandaloneSignatureHandle ResolveCallSite(IlMethodSignature signature) {
		InternalStateException.ThrowIfNull(signature);
		if (callsites.TryGetValue(signature, out StandaloneSignatureHandle cached))
			return cached;
		byte[] blob = SrmSignatureEncoder.EncodeMethodSignature(signature, this).ToArray();
		return callsites[signature] = MetadataTokens.StandaloneSignatureHandle(
			MetadataTokens.GetRowNumber(MetadataTokens.EntityHandle(lookupOrDefineStandaloneSignature(blob)))
		);
	}

	public StandaloneSignatureHandle ResolveLocals(ImmutableArray<IlTypeRef> locals, IlLocalSignatureOrigin origin) {
		if (locals.IsDefaultOrEmpty)
			return default;

		if (origin.IsValid && origin.Module == moduleIdentity) {
			LocalSignatureOriginHits++;
			return origin.Handle;
		}

		if (localSignatures.TryGetValue(locals, out StandaloneSignatureHandle cached))
			return cached;

		byte[] blob = SrmSignatureEncoder.EncodeLocalSignature(locals, this).ToArray();
		return localSignatures[locals] = MetadataTokens.StandaloneSignatureHandle(
			MetadataTokens.GetRowNumber(MetadataTokens.EntityHandle(lookupOrDefineStandaloneSignature(blob)))
		);
	}

	public UserStringHandle ResolveUserString(string value) {
		InternalStateException.ThrowIfNull(value);
		if (userStrings.TryGetValue(value, out UserStringHandle cached))
			return cached;

		// the user string heap has no readable index here, so strings are always defined
		// the emitter deduplicates, and the profiler's own heap does too, so a repeat costs a lookup rather than a row
		int token = emitter.DefineUserString(value);
		pendingDefinitions = true;
		return userStrings[value] = MetadataTokens.UserStringHandle(token & 0x00ffffff);
	}

	// ==========================================================================================
	// types
	private EntityHandle resolveNamedType(IlNamedTypeRef named) {
		string metadataName = named.GenericArity == 0
			? named.Name
			: $"{named.Name}`{named.GenericArity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

		EntityHandle? declaring = named.DeclaringType is IlNamedTypeRef parent ? ResolveType(parent) : null;

		// a type in this module is a TypeDef, and a nested one must be, because TypeRef.ResolutionScope cannot
		// code a TypeDef, so a nested type whose parent resolved to a definition has no valid TypeRef form at all
		if (declaring is EntityHandle handle ? handle.Kind == HandleKind.TypeDefinition : isSelfScope(named.Scope)) {
			typeDefinitionIndex ??= buildTypeDefinitionIndex();
			int enclosing = declaring is EntityHandle h ? MetadataTokens.GetToken(h) : 0;
			if (typeDefinitionIndex.TryGetValue(new TypeRowKey(enclosing, named.Namespace, metadataName), out int definition))
				return MetadataTokens.EntityHandle(definition);
			throw new IlEncodingException($"'{named}' is scoped to the target module, which defines no such type");
		}

		int scope = declaring is EntityHandle declaringHandle ? MetadataTokens.GetToken(declaringHandle) : resolutionScopeFor(named.Scope);

		typeReferenceIndex ??= buildTypeReferenceIndex();
		TypeRowKey key = new(scope, named.Namespace, metadataName);
		if (typeReferenceIndex.TryGetValue(key, out int existing))
			return MetadataTokens.EntityHandle(existing);

		int token = emitter.DefineTypeReference(scope, named.Namespace, metadataName);
		recordDefinition();
		typeReferenceIndex[key] = token;
		return MetadataTokens.EntityHandle(token);
	}

	private EntityHandle resolveGlobalModuleType() {
		typeDefinitionIndex ??= buildTypeDefinitionIndex();
		return typeDefinitionIndex.TryGetValue(new TypeRowKey(0, "", "<Module>"), out int token)
			? MetadataTokens.EntityHandle(token)
			: throw new IlEncodingException("the target module has no global module type");
	}

	private bool isSelfScope(IlTypeScope scope) => scope switch {
		IlTypeScope.Module module => module.Identity == moduleIdentity,
		IlTypeScope.Assembly assembly => selfAssembly is IlAssemblyIdentity self && assembly.Identity == self,
		_ => false,
	};

	private int resolutionScopeFor(IlTypeScope scope) {
		switch (scope) {
		case IlTypeScope.Assembly assembly: {
			if (assemblyReferences.TryGetValue(assembly.Identity, out int cached))
				return cached;
			assemblyReferenceIndex ??= buildAssemblyReferenceIndex();
			if (!assemblyReferenceIndex.TryGetValue(assembly.Identity, out int token)) {
				token = emitter.DefineAssemblyReference(assembly.Identity);
				recordDefinition();
				assemblyReferenceIndex[assembly.Identity] = token;
			}
			assemblyReferences[assembly.Identity] = token;
			return token;
		}
		case IlTypeScope.Module module when module.Identity == moduleIdentity:
			return 0;
		case IlTypeScope.Module module:
			return moduleReferenceFor(module.Identity.Name);
		case IlTypeScope.ModuleReference moduleReference:
			return moduleReferenceFor(moduleReference.Name);
		default:
			throw new InternalStateException($"unknown IlTypeScope derived type '{scope.GetType()}'");
		}
	}

	private Dictionary<IlAssemblyIdentity, int> buildAssemblyReferenceIndex() {
		Dictionary<IlAssemblyIdentity, int> index = new();
		foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
			index.TryAdd(decoder.ResolveAssemblyIdentity(handle), MetadataTokens.GetToken(handle));
		return index;
	}

	private int moduleReferenceFor(string name) {
		if (moduleReferences.TryGetValue(name, out int cached))
			return cached;
		foreach (ModuleReferenceHandle handle in rows(TableIndex.ModuleRef, MetadataTokens.ModuleReferenceHandle))
			if (metadata.GetString(metadata.GetModuleReference(handle).Name) == name) {
				int existing = MetadataTokens.GetToken(handle);
				moduleReferences[name] = existing;
				return existing;
			}
		throw new IlEncodingException($"the target module does not reference a module named '{name}', and module references cannot be added");
	}

	// ==========================================================================================
	// members and blobs
	private int lookupOrDefineMember(int parent, string name, byte[] signature) {
		memberReferenceIndex ??= buildMemberReferenceIndex();
		MemberRowKey key = new(parent, name, new BlobKey(signature));
		if (memberReferenceIndex.TryGetValue(key, out int existing))
			return existing;

		int token = emitter.DefineMemberReference(parent, name, signature);
		recordDefinition();
		memberReferenceIndex[key] = token;
		return token;
	}

	private int lookupOrDefineMethodSpecification(int method, byte[] signature) {
		methodSpecificationIndex ??= buildMethodSpecificationIndex();
		MemberRowKey key = new(method, "", new BlobKey(signature));
		if (methodSpecificationIndex.TryGetValue(key, out int existing))
			return existing;

		int token = emitter.DefineMethodSpecification(method, signature);
		recordDefinition();
		methodSpecificationIndex[key] = token;
		return token;
	}

	private int lookupOrDefineTypeSpecification(BlobBuilder signature) {
		byte[] blob = signature.ToArray();
		typeSpecificationIndex ??= buildBlobIndex(
			TableIndex.TypeSpec,
			rowId => metadata.GetTypeSpecification(MetadataTokens.TypeSpecificationHandle(rowId)).Signature
		);
		BlobKey key = new(blob);
		if (typeSpecificationIndex.TryGetValue(key, out int existing))
			return existing;

		int token = emitter.DefineTypeSpecification(blob);
		recordDefinition();
		typeSpecificationIndex[key] = token;
		return token;
	}

	private int lookupOrDefineStandaloneSignature(byte[] blob) {
		standaloneSignatureIndex ??= buildBlobIndex(
			TableIndex.StandAloneSig,
			rowId => metadata.GetStandaloneSignature(MetadataTokens.StandaloneSignatureHandle(rowId)).Signature
		);
		BlobKey key = new(blob);
		if (standaloneSignatureIndex.TryGetValue(key, out int existing))
			return existing;

		int token = emitter.DefineStandaloneSignature(blob);
		recordDefinition();
		standaloneSignatureIndex[key] = token;
		return token;
	}

	private void recordDefinition() {
		pendingDefinitions = true;
		DefinedRowCount++;
	}

	// ==========================================================================================
	// indexes over the module's original metadata
	private Dictionary<TypeRowKey, int> buildTypeReferenceIndex() {
		Dictionary<TypeRowKey, int> index = new();
		foreach (TypeReferenceHandle handle in metadata.TypeReferences) {
			TypeReference reference = metadata.GetTypeReference(handle);
			TypeRowKey key = new(
				reference.ResolutionScope.IsNil ? 0 : MetadataTokens.GetToken(reference.ResolutionScope),
				metadata.GetString(reference.Namespace),
				metadata.GetString(reference.Name)
			);
			index.TryAdd(key, MetadataTokens.GetToken(handle));
		}
		return index;
	}

	private Dictionary<TypeRowKey, int> buildTypeDefinitionIndex() {
		Dictionary<TypeRowKey, int> index = new();
		foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions) {
			TypeDefinition definition = metadata.GetTypeDefinition(handle);
			TypeDefinitionHandle declaring = definition.GetDeclaringType();
			TypeRowKey key = new(
				declaring.IsNil ? 0 : MetadataTokens.GetToken(declaring),
				metadata.GetString(definition.Namespace),
				metadata.GetString(definition.Name)
			);
			index.TryAdd(key, MetadataTokens.GetToken(handle));
		}
		return index;
	}

	private Dictionary<MemberRowKey, int> buildMemberReferenceIndex() {
		Dictionary<MemberRowKey, int> index = new();
		foreach (MemberReferenceHandle handle in metadata.MemberReferences) {
			MemberReference reference = metadata.GetMemberReference(handle);
			MemberRowKey key = new(
				reference.Parent.IsNil ? 0 : MetadataTokens.GetToken(reference.Parent),
				metadata.GetString(reference.Name),
				new BlobKey(metadata.GetBlobBytes(reference.Signature))
			);
			index.TryAdd(key, MetadataTokens.GetToken(handle));
		}
		return index;
	}

	private Dictionary<MemberRowKey, int> buildMethodSpecificationIndex() {
		Dictionary<MemberRowKey, int> index = new();
		foreach (MethodSpecificationHandle handle in rows(TableIndex.MethodSpec, MetadataTokens.MethodSpecificationHandle)) {
			MethodSpecification specification = metadata.GetMethodSpecification(handle);
			MemberRowKey key = new(
				specification.Method.IsNil ? 0 : MetadataTokens.GetToken(specification.Method),
				"",
				new BlobKey(metadata.GetBlobBytes(specification.Signature))
			);
			index.TryAdd(key, MetadataTokens.GetToken(handle));
		}
		return index;
	}

	private Dictionary<BlobKey, int> buildBlobIndex(TableIndex table, Func<int, BlobHandle> signatureOfRow) {
		Dictionary<BlobKey, int> index = new();
		int count = metadata.GetTableRowCount(table);
		for (int rowId = 1; rowId <= count; rowId++)
			index.TryAdd(new BlobKey(metadata.GetBlobBytes(signatureOfRow(rowId))), ((int)table << 24) | rowId);
		return index;
	}

	private IEnumerable<THandle> rows<THandle>(TableIndex table, Func<int, THandle> toHandle) {
		int count = metadata.GetTableRowCount(table);
		for (int rowId = 1; rowId <= count; rowId++)
			yield return toHandle(rowId);
	}
}
