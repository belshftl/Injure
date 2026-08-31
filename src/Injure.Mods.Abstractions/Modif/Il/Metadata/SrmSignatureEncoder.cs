// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Injure.Mods.Abstractions.Modif.Il.Metadata;

/// <summary>
/// Encodes references and signatures into ECMA-335 signature blobs.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <c>SrmReferenceDecoder</c>. The runtime needs this because
/// <c>IMetaDataEmit2::DefineMemberRef</c> and <c>GetTokenFromSig</c> take in raw blobs, so every
/// reference a manipulator constructs has to become bytes before it can become a token.
/// </para>
/// <para>
/// Blobs are written directly rather than through <see cref="BlobEncoder"/>; the reason is that
/// <see cref="BlobEncoder.MethodSignature(SignatureCallingConvention, int, bool)"/> has no
/// <c>EXPLICITTHIS</c> parameter, and using SRM's nested-struct protocol for caller supplied
/// <see cref="IlModifiedTypeRef"/> chains is more code than just writing the bytes.
/// The grammar being implemented is ECMA-335 II.23.2.
/// </para>
/// <para>
/// Encoding has a single canonical form, where compressed integers have a shortest form they use,
/// coded indices have one representation, sentinel placement is fixed, etc. This means that a
/// signature decoded from metadata and re-encoded by this should be byte-identical to the original,
/// which makes round-trip tests viable.
/// </para>
/// </remarks>
internal static class SrmSignatureEncoder {
	// II.23.1.16
	private const byte elementTypeVoid = 0x01;
	private const byte elementTypePointer = 0x0f;
	private const byte elementTypeByRef = 0x10;
	private const byte elementTypeValueType = 0x11;
	private const byte elementTypeClass = 0x12;
	private const byte elementTypeVar = 0x13;
	private const byte elementTypeArray = 0x14;
	private const byte elementTypeGenericInst = 0x15;
	private const byte elementTypeTypedByRef = 0x16;
	private const byte elementTypeFnPtr = 0x1b;
	private const byte elementTypeSzArray = 0x1d;
	private const byte elementTypeMvar = 0x1e;
	private const byte elementTypeCModRequired = 0x1f;
	private const byte elementTypeCModOptional = 0x20;
	private const byte elementTypeSentinel = 0x41;
	private const byte elementTypePinned = 0x45;

	// II.23.2.1 through II.23.2.15
	private const byte callConvGenericInstance = 0x0a;
	private const byte callConvField = 0x06;
	private const byte callConvLocalSig = 0x07;
	private const byte callConvGeneric = 0x10;
	private const byte callConvHasThis = 0x20;
	private const byte callConvExplicitThis = 0x40;

	/// <summary>
	/// Encodes a <c>MethodDefSig</c>, <c>MethodRefSig</c>, or <c>StandAloneMethodSig</c>.
	/// </summary>
	/// <remarks>
	/// A sentinel is written when <see cref="IlMethodSignature.RequiredParameterCount"/> is less than
	/// the parameter count, which is exactly the callsite form of a vararg signature. A vararg
	/// definition has no optional parameters and, as such, no sentinel, so no extra info like a flag is
	/// needed to tell the two apart.
	/// </remarks>
	public static BlobBuilder EncodeMethodSignature(IlMethodSignature signature, IIlTokenResolver resolver) {
		InternalStateException.ThrowIfNull(signature);
		ArgumentNullException.ThrowIfNull(resolver);
		BlobBuilder builder = new();
		writeMethodSignature(builder, signature, resolver);
		return builder;
	}

	/// <summary>
	/// Encodes a <c>LocalVarSig</c>.
	/// </summary>
	public static BlobBuilder EncodeLocalSignature(ImmutableArray<IlTypeRef> locals, IIlTokenResolver resolver) {
		ArgumentNullException.ThrowIfNull(resolver);
		ImmutableArray<IlTypeRef> types = locals.IsDefault ? [] : locals;
		if (types.IsEmpty)
			throw new IlEncodingException("a local variable signature must declare at least one local");

		BlobBuilder builder = new();
		builder.WriteByte(callConvLocalSig);
		builder.WriteCompressedInteger(types.Length);
		foreach (IlTypeRef local in types)
			writeLocal(builder, local, resolver);
		return builder;
	}

	/// <summary>
	/// Encodes a <c>FieldSig</c>.
	/// </summary>
	public static BlobBuilder EncodeFieldSignature(IlTypeRef fieldType, IIlTokenResolver resolver) {
		InternalStateException.ThrowIfNull(fieldType);
		ArgumentNullException.ThrowIfNull(resolver);
		BlobBuilder builder = new();
		builder.WriteByte(callConvField);
		writeSlot(builder, fieldType, resolver, allowPinned: false);
		return builder;
	}

	/// <summary>
	/// Encodes a <c>TypeSpec</c> blob, which is a bare type with no header.
	/// </summary>
	public static BlobBuilder EncodeTypeSpecification(IlTypeRef type, IIlTokenResolver resolver) {
		InternalStateException.ThrowIfNull(type);
		ArgumentNullException.ThrowIfNull(resolver);
		BlobBuilder builder = new();
		writeType(builder, type, resolver);
		return builder;
	}

	/// <summary>
	/// Encodes a <c>MethodSpec</c> blob: the generic arguments of one instantiation.
	/// </summary>
	public static BlobBuilder EncodeMethodSpecification(ImmutableArray<IlTypeRef> arguments, IIlTokenResolver resolver) {
		ArgumentNullException.ThrowIfNull(resolver);
		ImmutableArray<IlTypeRef> args = arguments.IsDefault ? [] : arguments;
		if (args.IsEmpty)
			throw new IlEncodingException("a method instantiation must supply at least one generic argument");

		BlobBuilder builder = new();
		builder.WriteByte(callConvGenericInstance);
		builder.WriteCompressedInteger(args.Length);
		foreach (IlTypeRef argument in args)
			writeType(builder, argument, resolver);
		return builder;
	}

	// ==========================================================================================
	// method signatures
	private static void writeMethodSignature(BlobBuilder builder, IlMethodSignature signature, IIlTokenResolver resolver) {
		int parameterCount = signature.ParameterTypes.Length;
		int required = signature.RequiredParameterCount;
		if ((uint)required > (uint)parameterCount)
			throw new IlEncodingException($"signature declares {required} required parameter(s) but only {parameterCount} parameter(s)");
		if (required < parameterCount && signature.CallingConvention != SignatureCallingConvention.VarArgs)
			throw new IlEncodingException("only a vararg signature may declare optional parameters");
		if (signature.ExplicitThis && !signature.HasThis)
			throw new IlEncodingException("a signature cannot be explicit-this without being instance");

		byte header = (byte)signature.CallingConvention;
		if (signature.HasThis)
			header |= callConvHasThis;
		if (signature.ExplicitThis)
			header |= callConvExplicitThis;
		if (signature.GenericParameterCount > 0)
			header |= callConvGeneric;
		builder.WriteByte(header);

		if (signature.GenericParameterCount > 0)
			builder.WriteCompressedInteger(signature.GenericParameterCount);
		builder.WriteCompressedInteger(parameterCount);

		writeSlot(builder, signature.ReturnType, resolver, allowPinned: false, allowVoid: true);
		for (int i = 0; i < parameterCount; i++) {
			if (i == required)
				builder.WriteByte(elementTypeSentinel);
			writeSlot(builder, signature.ParameterTypes[i], resolver, allowPinned: false);
		}
	}

	// ==========================================================================================
	// slots: positions where the prefix wrappers are legal
	private static void writeLocal(BlobBuilder builder, IlTypeRef local, IIlTokenResolver resolver) =>
		writeSlot(builder, local, resolver, allowPinned: true);

	/// <summary>
	/// Writes a parameter, return type, field type, or local: a type preceded by any custom
	/// modifiers, and optionally a pinned or byref marker.
	/// </summary>
	/// <remarks>
	/// The grammar is <c>CustomMod* [PINNED] [BYREF] Type</c>, and the model nests those wrappers in
	/// the same order, so peeling outside-in produces the right byte order. The loop still accepts
	/// them in any order because a hand-built reference might not do that.
	/// </remarks>
	private static void writeSlot(
		BlobBuilder builder,
		IlTypeRef type,
		IIlTokenResolver resolver,
		bool allowPinned,
		bool allowVoid = false
	) {
		InternalStateException.ThrowIfNull(type);
		bool isPinned = false;
		bool isByRef = false;

		for (;;) {
			switch (type) {
			case IlModifiedTypeRef modified:
				builder.WriteByte(modified.IsRequired ? elementTypeCModRequired : elementTypeCModOptional);
				writeTypeHandle(builder, modified.Modifier, resolver);
				type = modified.UnmodifiedType;
				continue;
			case IlPinnedTypeRef pinned:
				if (!allowPinned)
					throw new IlEncodingException("a pinned type is only valid as a local variable");
				if (isPinned)
					throw new IlEncodingException("a type cannot be pinned twice");
				builder.WriteByte(elementTypePinned);
				isPinned = true;
				type = pinned.ElementType;
				continue;
			case IlByRefTypeRef byRef:
				if (isByRef)
					throw new IlEncodingException("a byref type cannot be nested in another byref type");
				builder.WriteByte(elementTypeByRef);
				isByRef = true;
				type = byRef.ElementType;
				continue;
			}
			break;
		}

		if (type is IlPrimitiveTypeRef { Code: PrimitiveTypeCode.Void }) {
			if (!allowVoid || isByRef)
				throw new IlEncodingException("void is only valid as a return type or a pointer target");
			builder.WriteByte(elementTypeVoid);
			return;
		}
		writeType(builder, type, resolver);
	}

	// ==========================================================================================
	// types
	private static void writeType(BlobBuilder builder, IlTypeRef type, IIlTokenResolver resolver) {
		InternalStateException.ThrowIfNull(type);
		switch (type) {
		case IlPrimitiveTypeRef primitive:
			writePrimitive(builder, primitive.Code);
			return;
		case IlNamedTypeRef named:
			builder.WriteByte(isValueType(named) ? elementTypeValueType : elementTypeClass);
			writeTypeHandle(builder, named, resolver);
			return;
		case IlGenericInstanceTypeRef genericInst:
			builder.WriteByte(elementTypeGenericInst);
			builder.WriteByte(isValueType(genericInst.GenericType) ? elementTypeValueType : elementTypeClass);
			writeTypeHandle(builder, genericInst.GenericType, resolver);
			builder.WriteCompressedInteger(genericInst.Arguments.Length);
			foreach (IlTypeRef argument in genericInst.Arguments)
				writeType(builder, argument, resolver);
			return;
		case IlGenericParameterTypeRef genericParam:
			builder.WriteByte(
				genericParam.Kind == IlGenericParameterKind.Method ? elementTypeMvar : elementTypeVar
			);
			builder.WriteCompressedInteger(genericParam.Index);
			return;
		case IlSzArrayTypeRef szArray:
			builder.WriteByte(elementTypeSzArray);
			writeElement(builder, szArray.ElementType, resolver, allowVoid: false);
			return;
		case IlArrayTypeRef array:
			writeArray(builder, array, resolver);
			return;
		case IlPointerTypeRef pointer:
			builder.WriteByte(elementTypePointer);
			writeElement(builder, pointer.ElementType, resolver, allowVoid: true);
			return;
		case IlFunctionPointerTypeRef functionPointer:
			builder.WriteByte(elementTypeFnPtr);
			writeMethodSignature(builder, functionPointer.Signature, resolver);
			return;
		case IlModifiedTypeRef modified:
			// the grammar has CustomMod* only on Param, RetType, Field, LocalVar, PTR, and SZARRAY,
			// but tests with real metadata seems to suggest otherwise, even corelib has one

			// throw new IlEncodingException("a custom modifier is only valid on a parameter, return type, field, local, pointer, or szarray element");
			builder.WriteByte(modified.IsRequired ? elementTypeCModRequired : elementTypeCModOptional);
			writeTypeHandle(builder, modified.Modifier, resolver);
			writeType(builder, modified.UnmodifiedType, resolver);
			return;
		case IlByRefTypeRef:
			throw new IlEncodingException("a byref type is only valid as a parameter, return type, field, or local");
		case IlPinnedTypeRef:
			throw new IlEncodingException("a pinned type is only valid as a local variable");
		case IlGlobalModuleTypeRef:
			throw new IlEncodingException("the global module type cannot appear in a signature");
		default:
			throw new InternalStateException($"unknown IlTypeRef derived type '{type.GetType()}'");
		}
	}

	/// <summary>
	/// Writes <c>CustomMod* Type</c>, the element production shared by <c>PTR</c> and <c>SZARRAY</c>.
	/// </summary>
	/// <remarks>
	/// Modifiers can precede the element type, so the void check has to look through the whole chain.
	/// <c>void modopt(X)*</c> is a legal pointer target with its encoding being <c>PTR CMOD_OPT X VOID</c>.
	/// </remarks>
	private static void writeElement(BlobBuilder builder, IlTypeRef type, IIlTokenResolver resolver, bool allowVoid) {
		InternalStateException.ThrowIfNull(type);
		while (type is IlModifiedTypeRef modified) {
			builder.WriteByte(modified.IsRequired ? elementTypeCModRequired : elementTypeCModOptional);
			writeTypeHandle(builder, modified.Modifier, resolver);
			type = modified.UnmodifiedType;
		}
		if (type is IlPrimitiveTypeRef { Code: PrimitiveTypeCode.Void }) {
			if (!allowVoid)
				throw new IlEncodingException("void is only valid as a return type or a pointer target");
			builder.WriteByte(elementTypeVoid);
			return;
		}
		writeType(builder, type, resolver);
	}

	private static void writeArray(BlobBuilder builder, IlArrayTypeRef array, IIlTokenResolver resolver) {
		if (array.Rank < 1)
			throw new IlEncodingException($"array rank of {array.Rank} is not valid");
		builder.WriteByte(elementTypeArray);
		writeType(builder, array.ElementType, resolver);
		builder.WriteCompressedInteger(array.Rank);

		ImmutableArray<int> sizes = array.Sizes.IsDefault ? [] : array.Sizes;
		ImmutableArray<int> lowerBounds = array.LowerBounds.IsDefault ? [] : array.LowerBounds;
		if (sizes.Length > array.Rank || lowerBounds.Length > array.Rank)
			throw new IlEncodingException("array declares more sizes or lower bounds than it has dimensions");

		builder.WriteCompressedInteger(sizes.Length);
		foreach (int size in sizes) {
			if (size < 0)
				throw new IlEncodingException($"array dimension size of {size} is not valid");
			builder.WriteCompressedInteger(size);
		}
		builder.WriteCompressedInteger(lowerBounds.Length);
		foreach (int lowerBound in lowerBounds)
			builder.WriteCompressedSignedInteger(lowerBound);
	}

	private static void writePrimitive(BlobBuilder builder, PrimitiveTypeCode code) {
		// the primitive type codes are defined to equal their element type values
		switch (code) {
		case PrimitiveTypeCode.Boolean:
		case PrimitiveTypeCode.Char:
		case PrimitiveTypeCode.SByte:
		case PrimitiveTypeCode.Byte:
		case PrimitiveTypeCode.Int16:
		case PrimitiveTypeCode.UInt16:
		case PrimitiveTypeCode.Int32:
		case PrimitiveTypeCode.UInt32:
		case PrimitiveTypeCode.Int64:
		case PrimitiveTypeCode.UInt64:
		case PrimitiveTypeCode.Single:
		case PrimitiveTypeCode.Double:
		case PrimitiveTypeCode.String:
		case PrimitiveTypeCode.IntPtr:
		case PrimitiveTypeCode.UIntPtr:
		case PrimitiveTypeCode.Object:
			builder.WriteByte((byte)code);
			return;
		case PrimitiveTypeCode.TypedReference:
			builder.WriteByte(elementTypeTypedByRef);
			return;
		case PrimitiveTypeCode.Void:
			throw new IlEncodingException("void is only valid as a return type or a pointer target");
		default:
			throw new IlEncodingException($"primitive type code '{code}' cannot be encoded");
		}
	}

	/// <summary>
	/// Writes a <c>TypeDefOrRefOrSpecEncoded</c> coded index for a type that must resolve to a row.
	/// </summary>
	private static void writeTypeHandle(BlobBuilder builder, IlTypeRef type, IIlTokenResolver resolver) {
		EntityHandle handle = resolver.ResolveType(type);
		if (handle.IsNil)
			throw new IlEncodingException($"the token resolver returned a nil handle for '{type}'");
		if (handle.Kind is not (HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification))
			throw new IlEncodingException($"the token resolver returned a {handle.Kind} handle for '{type}'");
		builder.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(handle));
	}

	/// <summary>
	/// Whether a named type is encoded as <c>VALUETYPE</c> rather than <c>CLASS</c>.
	/// </summary>
	/// <remarks>
	/// The distinction is not recoverable from the name, and a signature carrying the wrong one names
	/// a different type, so <see cref="IlNamedTypeKind.Unknown"/> is rejected. That rejection should
	/// only ever be hit by hand-built references; every decoded reference carries a real kind.
	/// </remarks>
	private static bool isValueType(IlNamedTypeRef named) => named.TypeKind.Tag switch {
		IlNamedTypeKind.Case.ValueType => true,
		IlNamedTypeKind.Case.Class => false,
		_ => throw new IlEncodingException(
			$"'{named}' has an unknown type kind, so it cannot be encoded; construct it as a class or a value type"
		),
	};
}
