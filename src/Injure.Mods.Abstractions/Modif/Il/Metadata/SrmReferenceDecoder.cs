// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Injure.Mods.Abstractions.Modif.Il.Metadata;

internal readonly struct IlGenericContext;

internal sealed class SrmReferenceDecoder : ISignatureTypeProvider<IlTypeRef, IlGenericContext> {
	private readonly MetadataReader reader;
	private readonly IlModuleIdentity moduleIdentity;
	private readonly IlTypeScope moduleScope;

	public SrmReferenceDecoder(MetadataReader reader) {
		this.reader = reader;
		ModuleDefinition module = reader.GetModuleDefinition();
		moduleIdentity = new IlModuleIdentity(reader.GetGuid(module.Mvid), reader.GetString(module.Name));
		moduleScope = reader.IsAssembly
			? new IlTypeScope.Assembly(IlAssemblyIdentityFactory.FromDefinition(reader, reader.GetAssemblyDefinition()))
			: new IlTypeScope.Module(moduleIdentity);
	}

	public IlModuleIdentity ModuleIdentity => moduleIdentity;

	public IlTypeRef ResolveType(EntityHandle handle) => handle.Kind switch {
		HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, rawTypeKind: 0),
		HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, rawTypeKind: 0),
		HandleKind.TypeSpecification => GetTypeFromSpecification(reader, default, (TypeSpecificationHandle)handle, rawTypeKind: 0),
		_ => throw new BadImageFormatException($"token 0x{MetadataTokens.GetToken(handle):x8} is not a type token"),
	};

	public IlMethodRef ResolveMethod(EntityHandle handle) => handle.Kind switch {
		HandleKind.MethodDefinition => resolveMethodDefinition((MethodDefinitionHandle)handle),
		HandleKind.MemberReference => resolveMethodMemberReference((MemberReferenceHandle)handle),
		HandleKind.MethodSpecification => resolveMethodSpecification((MethodSpecificationHandle)handle),
		_ => throw new BadImageFormatException($"token 0x{MetadataTokens.GetToken(handle):x8} is not a method token"),
	};

	public IlFieldRef ResolveField(EntityHandle handle) => handle.Kind switch {
		HandleKind.FieldDefinition => resolveFieldDefinition((FieldDefinitionHandle)handle),
		HandleKind.MemberReference => resolveFieldMemberReference((MemberReferenceHandle)handle),
		_ => throw new BadImageFormatException($"token 0x{MetadataTokens.GetToken(handle):x8} is not a field token"),
	};

	public IlMethodSignature ResolveCallSite(StandaloneSignatureHandle handle) {
		StandaloneSignature signature = reader.GetStandaloneSignature(handle);
		if (signature.GetKind() != StandaloneSignatureKind.Method)
			throw new BadImageFormatException("calli token does not reference a method standalone signature");
		return convert(signature.DecodeMethodSignature(this, default));
	}

	public ImmutableArray<IlTypeRef> ResolveLocals(StandaloneSignatureHandle handle) {
		if (handle.IsNil)
			return [];
		StandaloneSignature signature = reader.GetStandaloneSignature(handle);
		if (signature.GetKind() != StandaloneSignatureKind.LocalVariables)
			throw new BadImageFormatException("method local signature token is not a local-variable signature");
		return signature.DecodeLocalSignature(this, default);
	}

	public IlTypeRef GetArrayType(IlTypeRef elementType, ArrayShape shape) =>
		new IlArrayTypeRef(elementType, shape.Rank, shape.Sizes, shape.LowerBounds);
	public IlTypeRef GetByReferenceType(IlTypeRef elementType) => new IlByRefTypeRef(elementType);
	public IlTypeRef GetPointerType(IlTypeRef elementType) => new IlPointerTypeRef(elementType);
	public IlTypeRef GetSZArrayType(IlTypeRef elementType) => new IlSzArrayTypeRef(elementType);
	public IlTypeRef GetPinnedType(IlTypeRef elementType) => new IlPinnedTypeRef(elementType);
	public IlTypeRef GetFunctionPointerType(MethodSignature<IlTypeRef> signature) => new IlFunctionPointerTypeRef(convert(signature));
	public IlTypeRef GetGenericInstantiation(IlTypeRef genericType, ImmutableArray<IlTypeRef> typeArguments) =>
		new IlGenericInstanceTypeRef(
			genericType as IlNamedTypeRef ?? throw new BadImageFormatException("genericinst does not reference a named type"),
			typeArguments
		);
	public IlTypeRef GetGenericMethodParameter(IlGenericContext genericContext, int index) =>
		new IlGenericParameterTypeRef(IlGenericParameterKind.Method, index);
	public IlTypeRef GetGenericTypeParameter(IlGenericContext genericContext, int index) =>
		new IlGenericParameterTypeRef(IlGenericParameterKind.Type, index);
	public IlTypeRef GetModifiedType(IlTypeRef modifier, IlTypeRef unmodifiedType, bool isRequired) =>
		new IlModifiedTypeRef(modifier, unmodifiedType, isRequired);
	public IlTypeRef GetPrimitiveType(PrimitiveTypeCode typeCode) => new IlPrimitiveTypeRef(typeCode);

	public IlTypeRef GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind) =>
		normalizeKnownPrimitive(namedTypeFromDefinition(metadataReader, handle, rawTypeKind));

	public IlTypeRef GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind) =>
		normalizeKnownPrimitive(namedTypeFromReference(metadataReader, handle, rawTypeKind));

	/// <summary>
	/// Maps a top-level named type onto its primitive form, if it has one.
	/// </summary>
	/// <remarks>
	/// Only ever applied at a signature position. A declaring type is always kept in named form, both
	/// because a nested type's parent is structurally an <see cref="IlNamedTypeRef"/> and because a
	/// type that only appears as a parent never occupies a signature position of its own.
	/// </remarks>
	private static IlTypeRef normalizeKnownPrimitive(IlNamedTypeRef named) =>
		named.DeclaringType is null && named.GenericArity == 0 &&
		tryKnownPrimitive(named.Namespace, named.Name, out PrimitiveTypeCode primitive)
			? new IlPrimitiveTypeRef(primitive)
			: named;

	private IlNamedTypeRef namedTypeFromDefinition(
		MetadataReader metadataReader,
		TypeDefinitionHandle handle,
		byte rawTypeKind
	) {
		TypeDefinition definition = metadataReader.GetTypeDefinition(handle);
		TypeDefinitionHandle declaringHandle = definition.GetDeclaringType();
		IlNamedTypeRef? declaring = declaringHandle.IsNil
			? null
			: namedTypeFromDefinition(metadataReader, declaringHandle, rawTypeKind: 0);
		string @namespace = metadataReader.GetString(definition.Namespace);
		(string name, int arity) = splitMetadataName(metadataReader.GetString(definition.Name));
		IlNamedTypeKind kind = toNamedTypeKind(metadataReader.ResolveSignatureTypeKind(handle, rawTypeKind));
		if (kind == IlNamedTypeKind.Unknown)
			kind = inferDefinitionKind(definition);
		return new IlNamedTypeRef(moduleScope, declaring, @namespace, name, arity, kind);
	}

	private IlNamedTypeRef namedTypeFromReference(
		MetadataReader metadataReader,
		TypeReferenceHandle handle,
		byte rawTypeKind
	) {
		TypeReference reference = metadataReader.GetTypeReference(handle);
		IlNamedTypeRef? declaring = null;
		IlTypeScope scope;
		switch (reference.ResolutionScope.Kind) {
		case HandleKind.TypeReference:
			declaring = namedTypeFromReference(
				metadataReader,
				(TypeReferenceHandle)reference.ResolutionScope,
				rawTypeKind: 0
			);
			scope = declaring.Scope;
			break;
		case HandleKind.AssemblyReference:
			scope = new IlTypeScope.Assembly(ResolveAssemblyIdentity((AssemblyReferenceHandle)reference.ResolutionScope));
			break;
		case HandleKind.ModuleReference:
			scope = new IlTypeScope.ModuleReference(
				metadataReader.GetString(
					metadataReader.GetModuleReference((ModuleReferenceHandle)reference.ResolutionScope).Name
				)
			);
			break;
		case HandleKind.ModuleDefinition:
			scope = moduleScope;
			break;
		default:
			throw new BadImageFormatException($"unsupported TypeRef resolution scope {reference.ResolutionScope.Kind}");
		}
		string @namespace = metadataReader.GetString(reference.Namespace);
		(string name, int arity) = splitMetadataName(metadataReader.GetString(reference.Name));
		IlNamedTypeKind kind = toNamedTypeKind(metadataReader.ResolveSignatureTypeKind(handle, rawTypeKind));
		return new IlNamedTypeRef(scope, declaring, @namespace, name, arity, kind);
	}

	public IlTypeRef GetTypeFromSpecification(
		MetadataReader metadataReader,
		IlGenericContext genericContext,
		TypeSpecificationHandle handle,
		byte rawTypeKind
	) => metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

	private IlMethodRef resolveMethodDefinition(MethodDefinitionHandle handle) {
		MethodDefinition definition = reader.GetMethodDefinition(handle);
		IlTypeRef declaringType = ResolveType(definition.GetDeclaringType());
		return new IlMethodRef(
			declaringType,
			reader.GetString(definition.Name),
			convert(definition.DecodeSignature(this, default)),
			[]
		);
	}

	private IlMethodRef resolveMethodMemberReference(MemberReferenceHandle handle) {
		MemberReference reference = reader.GetMemberReference(handle);
		if (reference.GetKind() != MemberReferenceKind.Method)
			throw new BadImageFormatException("member reference is not a method");
		return new IlMethodRef(
			resolveMemberParent(reference.Parent),
			reader.GetString(reference.Name),
			convert(reference.DecodeMethodSignature(this, default)),
			[]
		);
	}

	private IlMethodRef resolveMethodSpecification(MethodSpecificationHandle handle) {
		MethodSpecification specification = reader.GetMethodSpecification(handle);
		IlMethodRef definition = ResolveMethod(specification.Method);
		return new IlMethodRef(
			definition.DeclaringType,
			definition.Name,
			definition.Signature,
			specification.DecodeSignature(this, default)
		);
	}

	private IlFieldRef resolveFieldDefinition(FieldDefinitionHandle handle) {
		FieldDefinition definition = reader.GetFieldDefinition(handle);
		return new IlFieldRef(
			ResolveType(definition.GetDeclaringType()),
			reader.GetString(definition.Name),
			definition.DecodeSignature(this, default)
		);
	}

	private IlFieldRef resolveFieldMemberReference(MemberReferenceHandle handle) {
		MemberReference reference = reader.GetMemberReference(handle);
		if (reference.GetKind() != MemberReferenceKind.Field)
			throw new BadImageFormatException("member reference is not a field");
		return new IlFieldRef(
			resolveMemberParent(reference.Parent),
			reader.GetString(reference.Name),
			reference.DecodeFieldSignature(this, default)
		);
	}

	private IlTypeRef resolveMemberParent(EntityHandle parent) => parent.Kind switch {
		HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification => ResolveType(parent),
		HandleKind.MethodDefinition => ResolveMethod(parent).DeclaringType,
		HandleKind.ModuleReference => new IlGlobalModuleTypeRef(
			new IlTypeScope.ModuleReference(reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)parent).Name))
		),
		_ => throw new BadImageFormatException($"unsupported MemberRef parent {parent.Kind}"),
	};

	public IlAssemblyIdentity ResolveAssemblyIdentity(AssemblyReferenceHandle handle) =>
		IlAssemblyIdentityFactory.FromReference(reader, reader.GetAssemblyReference(handle));

	private static IlNamedTypeKind toNamedTypeKind(SignatureTypeKind kind) => kind switch {
		SignatureTypeKind.Class => IlNamedTypeKind.Class,
		SignatureTypeKind.ValueType => IlNamedTypeKind.ValueType,
		_ => IlNamedTypeKind.Unknown,
	};

	private IlNamedTypeKind inferDefinitionKind(TypeDefinition definition) {
		if (definition.BaseType.IsNil)
			return IlNamedTypeKind.Class;
		try {
			IlTypeRef baseType = ResolveType(definition.BaseType);
			if (baseType is IlNamedTypeRef named && named.Namespace == "System" && named.Name is "ValueType" or "Enum")
				return IlNamedTypeKind.ValueType;
		} catch (BadImageFormatException) {
		}
		return IlNamedTypeKind.Class;
	}

	private static IlMethodSignature convert(MethodSignature<IlTypeRef> signature) => new(
		signature.Header.CallingConvention,
		signature.Header.IsInstance,
		signature.Header.HasExplicitThis,
		signature.GenericParameterCount,
		signature.RequiredParameterCount,
		signature.ReturnType,
		signature.ParameterTypes
	);

	private static bool tryKnownPrimitive(string @namespace, string name, out PrimitiveTypeCode code) {
		code = default;
		if (@namespace != "System")
			return false;
		if (name == "Void") code = PrimitiveTypeCode.Void;
		else if (name == "Boolean") code = PrimitiveTypeCode.Boolean;
		else if (name == "Char") code = PrimitiveTypeCode.Char;
		else if (name == "SByte") code = PrimitiveTypeCode.SByte;
		else if (name == "Byte") code = PrimitiveTypeCode.Byte;
		else if (name == "Int16") code = PrimitiveTypeCode.Int16;
		else if (name == "UInt16") code = PrimitiveTypeCode.UInt16;
		else if (name == "Int32") code = PrimitiveTypeCode.Int32;
		else if (name == "UInt32") code = PrimitiveTypeCode.UInt32;
		else if (name == "Int64") code = PrimitiveTypeCode.Int64;
		else if (name == "UInt64") code = PrimitiveTypeCode.UInt64;
		else if (name == "Single") code = PrimitiveTypeCode.Single;
		else if (name == "Double") code = PrimitiveTypeCode.Double;
		else if (name == "String") code = PrimitiveTypeCode.String;
		else if (name == "Object") code = PrimitiveTypeCode.Object;
		else if (name == "IntPtr") code = PrimitiveTypeCode.IntPtr;
		else if (name == "UIntPtr") code = PrimitiveTypeCode.UIntPtr;
		else if (name == "TypedReference") code = PrimitiveTypeCode.TypedReference;
		else return false;
		return true;
	}

	private static (string Name, int Arity) splitMetadataName(string name) {
		int bt = name.LastIndexOf('`');
		if (bt < 0 || !int.TryParse(name.AsSpan(bt + 1), out int arity))
			return (name, 0);
		return (name[..bt], arity);
	}
}
