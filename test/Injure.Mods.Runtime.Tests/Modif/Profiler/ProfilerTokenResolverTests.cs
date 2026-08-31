// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Tests.Modif.Profiler;

public sealed class ProfilerTokenResolverTests : IDisposable {
	private readonly FakeProfilerHost host = new();
	private readonly ModuleInfo module;
	private readonly ProfilerTokenResolver resolver;
	private readonly FakeMetadataEmitter emitter;
	private readonly MetadataReader metadata;
	private readonly SrmReferenceDecoder decoder;

	public ProfilerTokenResolverTests() {
		string location = typeof(IlFixture.Mechanism).Assembly.Location;
		Assert.SkipWhen(string.IsNullOrEmpty(location), "fixture assembly has no on-disk location");
		module = host.LoadModule(location);
		metadata = host.GetMetadata(module.Id);
		emitter = host.GetEmitter(module.Id);
		decoder = new SrmReferenceDecoder(metadata);
		resolver = new ProfilerTokenResolver(metadata, emitter, decoder);
	}

	public void Dispose() => host.Dispose();

	// ==========================================================================================
	// reuse
	[Fact]
	public void ATypeTheModuleAlreadyReferencesDefinesNothing() {
		// the fixture references System.Type, so a TypeRef row already exists
		TypeReferenceHandle existing = firstTypeReference("System", "Type");
		IlTypeRef type = decoder.ResolveType(existing);

		EntityHandle resolved = resolver.ResolveType(type);

		Assert.Equal((EntityHandle)existing, resolved);
		Assert.Equal(0, emitter.CountDefined(TableIndex.TypeRef));
	}

	[Fact]
	public void ATypeDefinedInTheModuleResolvesToItsDefinition() {
		TypeDefinitionHandle existing = firstTypeDefinition("Mechanism");
		IlTypeRef type = decoder.ResolveType(existing);

		EntityHandle resolved = resolver.ResolveType(type);

		Assert.Equal(HandleKind.TypeDefinition, resolved.Kind);
		Assert.Equal((EntityHandle)existing, resolved);
		Assert.Empty(emitter.Defined);
		Assert.Equal(0, emitter.CountDefined(TableIndex.AssemblyRef));
	}

	[Fact]
	public void AMemberTheModuleAlreadyReferencesDefinesNothing() {
		IlMethodBody body = decodeBody("GenericCallers", "Closed");
		IlMethodRef call = soleCall(body, "Pick");

		resolver.ResolveMethod(call);

		Assert.Empty(emitter.Defined);
	}

	[Fact]
	public void ResolvingTheSameReferenceTwiceDefinesOneRow() {
		IlMethodRef method = newMethod("Ns", "Helper", "Run");

		EntityHandle first = resolver.ResolveMethod(method);
		EntityHandle second = resolver.ResolveMethod(method);

		Assert.Equal(first, second);
		Assert.Equal(1, emitter.CountDefined(TableIndex.MemberRef));
	}

	[Fact]
	public void TwoMembersOnOneNewTypeShareItsTypeReference() {
		resolver.ResolveMethod(newMethod("Ns", "Helper", "First"));
		resolver.ResolveMethod(newMethod("Ns", "Helper", "Second"));

		Assert.Equal(1, emitter.CountDefined(TableIndex.TypeRef));
		Assert.Equal(2, emitter.CountDefined(TableIndex.MemberRef));
	}

	// ==========================================================================================
	// cross-assembly references
	[Fact]
	public void ANewAssemblyIsReferencedBeforeItsTypeAndMember() {
		resolver.ResolveMethod(newMethod("Mod.Ns", "Patch", "Hook"));

		TableIndex[] order = [.. emitter.Defined.Select(d => d.Table)];
		Assert.Equal([TableIndex.AssemblyRef, TableIndex.TypeRef, TableIndex.MemberRef], order);
	}

	[Fact]
	public void TypesFromOneAssemblyShareItsReference() {
		resolver.ResolveMethod(newMethod("Mod.Ns", "First", "A"));
		resolver.ResolveMethod(newMethod("Mod.Ns", "Second", "B"));

		Assert.Equal(1, emitter.CountDefined(TableIndex.AssemblyRef));
		Assert.Equal(2, emitter.CountDefined(TableIndex.TypeRef));
	}

	[Fact]
	public void ANestedTypeIsScopedToItsDeclaringType() {
		IlNamedTypeRef declaring = named("Mod.Ns", "Outer");
		IlNamedTypeRef nested = new(declaring.Scope, declaring, "", "Inner", 0, IlNamedTypeKind.Class);

		EntityHandle declaringHandle = resolver.ResolveType(declaring);
		resolver.ResolveType(nested);

		// the second TypeRef's resolution scope is the first, not the assembly
		Assert.Equal(2, emitter.CountDefined(TableIndex.TypeRef));
		Assert.Contains(emitter.Defined, d => d.Token == MetadataTokens.GetToken(declaringHandle));
	}

	// ==========================================================================================
	// constructed types and instantiations
	[Fact]
	public void AConstructedTypeBecomesATypeSpecification() {
		IlTypeRef array = IlRefFactory.SzArray(new IlPrimitiveTypeRef(PrimitiveTypeCode.Int32));

		EntityHandle handle = resolver.ResolveType(array);

		Assert.Equal(HandleKind.TypeSpecification, handle.Kind);
	}

	[Fact]
	public void IdenticalConstructedTypesShareOneSpecification() {
		IlTypeRef first = IlRefFactory.SzArray(new IlPrimitiveTypeRef(PrimitiveTypeCode.Int64));
		IlTypeRef second = IlRefFactory.SzArray(new IlPrimitiveTypeRef(PrimitiveTypeCode.Int64));

		Assert.Equal(resolver.ResolveType(first), resolver.ResolveType(second));
		Assert.Equal(1, emitter.CountDefined(TableIndex.TypeSpec));
	}

	[Fact]
	public void AGenericInstantiationBecomesAMethodSpecificationOverAMemberReference() {
		IlNamedTypeRef declaring = named("Mod.Ns", "Helper");
		IlMethodRef definition = IlRefFactory.Method(
			declaring,
			"Run",
			IlRefFactory.Signature(
				returnType: IlRefFactory.MethodGenericParameter(0),
				parameterTypes: [],
				genericParameterCount: 1
			)
		);
		IlMethodRef instantiated = IlRefFactory.GenericMethod(
			definition,
			new IlPrimitiveTypeRef(PrimitiveTypeCode.Int32)
		);

		EntityHandle handle = resolver.ResolveMethod(instantiated);

		Assert.Equal(HandleKind.MethodSpecification, handle.Kind);
		Assert.Equal(1, emitter.CountDefined(TableIndex.MemberRef));
		Assert.Equal(1, emitter.CountDefined(TableIndex.MethodSpec));
	}

	[Fact]
	public void AMemberOnAGenericInstanceIsParentedToATypeSpecification() {
		IlNamedTypeRef definition = named("Mod.Ns", "Box", arity: 1);
		IlGenericInstanceTypeRef instance = IlRefFactory.GenericInstance(
			definition,
			new IlPrimitiveTypeRef(PrimitiveTypeCode.Int32)
		);
		IlMethodRef method = IlRefFactory.Method(
			instance,
			"Get",
			IlRefFactory.Signature(IlRefFactory.TypeGenericParameter(0))
		);

		resolver.ResolveMethod(method);

		Assert.Equal(1, emitter.CountDefined(TableIndex.TypeSpec));
		Assert.Equal(1, emitter.CountDefined(TableIndex.MemberRef));
	}

	// ==========================================================================================
	// locals
	[Fact]
	public void AnUnchangedLocalsListFromThisModuleDefinesNothing() {
		StandaloneSignatureHandle origin = firstLocalSignature();
		ImmutableArray<IlTypeRef> locals = decoder.ResolveLocals(origin);

		StandaloneSignatureHandle handle = resolver.ResolveLocals(
			locals,
			new IlLocalSignatureOrigin(decoder.ModuleIdentity, MetadataTokens.GetRowNumber(origin))
		);

		Assert.Equal(origin, handle);
		Assert.Equal(1, resolver.LocalSignatureOriginHits);
		Assert.Equal(0, emitter.CountDefined(TableIndex.StandAloneSig));
	}

	[Fact]
	public void AnOriginFromAnotherModuleIsIgnored() {
		IlLocalSignatureOrigin foreign = new(new IlModuleIdentity(Guid.NewGuid(), "Other"), 1);

		resolver.ResolveLocals([new IlPrimitiveTypeRef(PrimitiveTypeCode.Int32)], foreign);

		Assert.Equal(0, resolver.LocalSignatureOriginHits);
	}

	[Fact]
	public void AnEmptyLocalsListResolvesToNoSignature() {
		Assert.True(resolver.ResolveLocals([], default).IsNil);
		Assert.Empty(emitter.Defined);
	}

	[Fact]
	public void IdenticalLocalsListsShareOneSignature() {
		ImmutableArray<IlTypeRef> locals = [
			new IlPrimitiveTypeRef(PrimitiveTypeCode.Int32),
			new IlPrimitiveTypeRef(PrimitiveTypeCode.String),
		];

		StandaloneSignatureHandle first = resolver.ResolveLocals(locals, default);
		StandaloneSignatureHandle second = resolver.ResolveLocals([.. locals], default);

		Assert.Equal(first, second);
		Assert.Equal(1, emitter.CountDefined(TableIndex.StandAloneSig));
	}

	// ==========================================================================================
	// user strings and commits
	[Fact]
	public void UserStringsAreDeduplicated() {
		UserStringHandle first = resolver.ResolveUserString("hello");
		UserStringHandle second = resolver.ResolveUserString("hello");
		UserStringHandle other = resolver.ResolveUserString("world");

		Assert.Equal(first, second);
		Assert.NotEqual(first, other);
	}

	[Fact]
	public void CommitIsANoOpWhenNothingWasDefined() {
		resolver.Commit();

		Assert.Equal(0, emitter.Commits);
	}

	[Fact]
	public void CommitFlushesOnceForABatchOfDefinitions() {
		resolver.ResolveMethod(newMethod("Mod.Ns", "First", "A"));
		resolver.ResolveMethod(newMethod("Mod.Ns", "Second", "B"));
		resolver.Commit();
		resolver.Commit();

		Assert.Equal(1, emitter.Commits);
	}

	// ==========================================================================================
	// end to end
	[Fact]
	public void APatchedBodyEncodesAgainstResolvedTokens() {
		IlMethodBody baseline = decodeBody("GenericCallers", "Closed");
		IlEncodedMethodBody encoded = SrmMethodBodyEncoder.Prepare(baseline, resolver);
		resolver.Commit();

		Assert.True(encoded.Size > 0);
		// every token the body needed was already present in the module
		Assert.Empty(emitter.Defined);
	}

	// ==========================================================================================
	// helpers
	private IlMethodBody decodeBody(string declaringTypeName, string methodName) {
		MethodIdentity identity = host.FindMethod(module.Id, declaringTypeName, methodName);
		var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(identity.MethodDefToken);
		ImmutableArray<byte> il = host.GetBaselineIl(identity);
		unsafe {
			fixed (byte* p = il.AsSpan())
				return SrmMethodBodyDecoder.Decode(metadata, handle, new BlobReader(p, il.Length), default);
		}
	}

	private static IlMethodRef soleCall(IlMethodBody body, string name) =>
		body.Instructions
			.Where(static i => i.OpCode is ILOpCode.Call or ILOpCode.Callvirt)
			.Select(static i => ((IlMethodOperand)i.Operand).Method)
			.Single(m => m.Name == name);

	/// <summary>
	/// A named type in an assembly that the fixture does not reference, so resolving it always defines.
	/// </summary>
	private static IlNamedTypeRef named(string @namespace, string name, int arity = 0) => new(
		new IlTypeScope.Assembly(new IlAssemblyIdentity("Mod.Assembly", new Version(1, 0, 0, 0), null, default, default)),
		null,
		@namespace,
		name,
		arity,
		IlNamedTypeKind.Class
	);

	private static IlMethodRef newMethod(string @namespace, string typeName, string methodName) =>
		IlRefFactory.Method(
			named(@namespace, typeName),
			methodName,
			IlRefFactory.Signature(new IlPrimitiveTypeRef(PrimitiveTypeCode.Void))
		);

	private TypeReferenceHandle firstTypeReference(string @namespace, string name) {
		foreach (TypeReferenceHandle handle in metadata.TypeReferences) {
			TypeReference reference = metadata.GetTypeReference(handle);
			if (metadata.GetString(reference.Namespace) == @namespace && metadata.GetString(reference.Name) == name)
				return handle;
		}
		throw new InvalidOperationException($"{@namespace}.{name} is not referenced by the fixture");
	}

	private TypeDefinitionHandle firstTypeDefinition(string name) {
		foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
			if (metadata.GetString(metadata.GetTypeDefinition(handle).Name) == name)
				return handle;
		throw new InvalidOperationException($"{name} is not defined by the fixture");
	}

	private StandaloneSignatureHandle firstLocalSignature() {
		int count = metadata.GetTableRowCount(TableIndex.StandAloneSig);
		for (int rowId = 1; rowId <= count; rowId++) {
			StandaloneSignatureHandle handle = MetadataTokens.StandaloneSignatureHandle(rowId);
			BlobReader reader = metadata.GetBlobReader(metadata.GetStandaloneSignature(handle).Signature);
			if ((reader.ReadByte() & 0x0f) == 0x07)
				return handle;
		}
		throw new InvalidOperationException("the fixture has no local variable signatures");
	}
}
