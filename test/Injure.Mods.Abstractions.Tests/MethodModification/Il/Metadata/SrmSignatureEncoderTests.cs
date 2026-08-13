// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Mods.Abstractions.Tests.MethodModification.Il.Metadata;

public sealed class SrmSignatureEncoderTests {
	private static readonly FakeTokenResolver resolver = new();
	private static int typeCodedIndex(IlTypeRef type) =>
		CodedIndex.TypeDefOrRefOrSpec(resolver.ResolveType(type));
	private static byte[] method(IlMethodSignature signature) =>
		SrmSignatureEncoder.EncodeMethodSignature(signature, resolver).ToArray();
	private static byte[] typeSpec(IlTypeRef type) =>
		SrmSignatureEncoder.EncodeTypeSpecification(type, resolver).ToArray();

	// ==========================================================================================
	// headers
	[Fact]
	public static void StaticVoidSignatureMatches() =>
		// DEFAULT, 0 params, VOID
		Assert.Equal([0x00, 0x00, 0x01], method(IlTest.Sig(IlTest.Void)));

	[Fact]
	public static void InstanceSignatureSetsHasThis() =>
		// HASTHIS | DEFAULT, 1 param, I4 return, I4 param
		Assert.Equal([0x20, 0x01, 0x08, 0x08], method(IlTest.InstanceSig(IlTest.Int32, IlTest.Int32)));

	[Fact]
	public static void ExplicitThisSignatureSetsBothBits() =>
		Assert.Equal([0x60, 0x01, 0x01, 0x1c], method(IlTest.ExplicitThisSig(IlTest.Void, IlTest.Object)));

	[Fact]
	public static void GenericSignatureWritesItsParameterCount() {
		IlMethodSignature signature = IlReferenceFactory.Signature(
			returnType: IlReferenceFactory.MethodGenericParameter(0),
			parameterTypes: [IlReferenceFactory.MethodGenericParameter(1)],
			genericParameterCount: 2
		);

		// GENERIC | DEFAULT, 2 generic params, 1 param, MVAR 0, MVAR 1
		Assert.Equal([0x10, 0x02, 0x01, 0x1e, 0x00, 0x1e, 0x01], method(signature));
	}

	[Fact]
	public static void ExplicitThisWithoutHasThisIsRejected() {
		IlMethodSignature signature = new(SignatureCallingConvention.Default, false, true, 0, 0, IlTest.Void, []);

		Assert.Throws<IlEncodingException>(() => method(signature));
	}

	// ==========================================================================================
	// varargs
	[Fact]
	public static void VarargDefinitionHasNoSentinel() =>
		// VARARG, 1 param, I4 return, I4 param
		Assert.Equal([0x05, 0x01, 0x08, 0x08], method(IlTest.VarargSig(IlTest.Int32, required: 1, IlTest.Int32)));

	[Fact]
	public static void VarargCallsiteWritesASentinelBeforeTheOptionalParameters() =>
		// VARARG, 3 params, I4 return, I4, SENTINEL, I4, STRING
		Assert.Equal(
			[0x05, 0x03, 0x08, 0x08, 0x41, 0x08, 0x0e],
			method(IlTest.VarargSig(IlTest.Int32, required: 1, IlTest.Int32, IlTest.Int32, IlTest.String))
		);

	[Fact]
	public static void OptionalParametersRequireTheVarargConvention() {
		IlMethodSignature signature = new(
			SignatureCallingConvention.Default, false, false, 0, 0, IlTest.Void, [IlTest.Int32]
		);

		Assert.Throws<IlEncodingException>(() => method(signature));
	}

	// ==========================================================================================
	// primitives and void
	[Theory]
	[InlineData(PrimitiveTypeCode.Boolean, 0x02)]
	[InlineData(PrimitiveTypeCode.Char, 0x03)]
	[InlineData(PrimitiveTypeCode.Int32, 0x08)]
	[InlineData(PrimitiveTypeCode.UInt64, 0x0b)]
	[InlineData(PrimitiveTypeCode.Double, 0x0d)]
	[InlineData(PrimitiveTypeCode.String, 0x0e)]
	[InlineData(PrimitiveTypeCode.TypedReference, 0x16)]
	[InlineData(PrimitiveTypeCode.IntPtr, 0x18)]
	[InlineData(PrimitiveTypeCode.Object, 0x1c)]
	public static void PrimitivesEncodeAsTheirElementType(PrimitiveTypeCode code, byte expected) =>
		Assert.Equal([expected], typeSpec(new IlPrimitiveTypeRef(code)));

	[Fact]
	public static void VoidIsRejectedOutsideReturnTypeOrPointerTarget() {
		Assert.Throws<IlEncodingException>(() => typeSpec(IlTest.Void));
		Assert.Throws<IlEncodingException>(() => method(IlTest.Sig(IlTest.Int32, IlTest.Void)));
	}

	[Fact]
	public static void VoidPointerIsAccepted() =>
		Assert.Equal([0x0f, 0x01], typeSpec(IlReferenceFactory.Pointer(IlTest.Void)));

	// ==========================================================================================
	// named types and instantiations
	[Fact]
	public static void ClassWritesClassAndItsCodedIndex() {
		byte[] encoded = typeSpec(IlTest.Exception);

		Assert.Equal(0x12, encoded[0]);
		Assert.Equal(typeCodedIndex(IlTest.Exception), decompress(encoded, 1, out _));
	}

	[Fact]
	public static void ValueTypeWritesValueType() {
		IlNamedTypeRef named = IlTest.Named("System", "DateTime", IlNamedTypeKind.ValueType);
		Assert.Equal(0x11, typeSpec(named)[0]);
	}

	[Fact]
	public static void UnknownTypeKindIsRejected() {
		IlNamedTypeRef named = IlTest.Named("Ns", "T", IlNamedTypeKind.Unknown);

		Assert.Throws<IlEncodingException>(() => typeSpec(named));
	}

	[Fact]
	public static void GenericInstanceWritesDefinitionThenItsArguments() {
		IlNamedTypeRef definition = IlTest.Named("System.Collections.Generic", "List", IlNamedTypeKind.Class, genericArity: 1);
		IlGenericInstanceTypeRef instance = IlReferenceFactory.GenericInstance(definition, IlTest.Int32);
		byte[] encoded = typeSpec(instance);

		Assert.Equal(0x15, encoded[0]); // GENERICINST
		Assert.Equal(0x12, encoded[1]); // CLASS
		int index = 2;
		Assert.Equal(typeCodedIndex(definition), decompress(encoded, index, out index));
		Assert.Equal(1, encoded[index]);
		Assert.Equal(0x08, encoded[index + 1]);
	}

	[Fact]
	public static void GenericParametersDistinguishTypeFromMethod() {
		Assert.Equal([0x13, 0x02], typeSpec(IlReferenceFactory.TypeGenericParameter(2)));
		Assert.Equal([0x1e, 0x02], typeSpec(IlReferenceFactory.MethodGenericParameter(2)));
	}

	// ==========================================================================================
	// arrays and pointers
	[Fact]
	public static void SzArrayWrapsItsElementType() =>
		Assert.Equal([0x1d, 0x08], typeSpec(IlReferenceFactory.SzArray(IlTest.Int32)));

	[Fact]
	public static void MultidimensionalArrayWritesRankThenBounds() =>
		// ARRAY, I4, rank 2, 0 sizes, 0 lower bounds
		Assert.Equal([0x14, 0x08, 0x02, 0x00, 0x00], typeSpec(IlReferenceFactory.Array(IlTest.Int32, rank: 2)));

	[Fact]
	public static void ArraySizesAndLowerBoundsAreWrittenWhenPresent() {
		IlArrayTypeRef array = IlReferenceFactory.Array(IlTest.Int32, rank: 2, sizes: [3, 4], lowerBounds: [1, -1]);

		// ARRAY, I4, rank 2, 2 sizes, 3, 4, 2 bounds, +1, -1 (signed compressed)
		Assert.Equal([0x14, 0x08, 0x02, 0x02, 0x03, 0x04, 0x02, 0x02, 0x7f], typeSpec(array));
	}

	[Fact]
	public static void NestedPointerChainIsWrittenOutsideIn() =>
		Assert.Equal(
			[0x0f, 0x0f, 0x08],
			typeSpec(IlReferenceFactory.Pointer(IlReferenceFactory.Pointer(IlTest.Int32)))
		);

	// ==========================================================================================
	// modifiers, byref, pinned
	[Fact]
	public static void ModifiersPrecedeTheTypeTheyModify() {
		IlNamedTypeRef modifier = IlTest.Named("System.Runtime.CompilerServices", "IsVolatile", IlNamedTypeKind.Class);
		byte[] encoded = method(IlTest.Sig(IlReferenceFactory.RequiredModifier(modifier, IlTest.Int32)));

		Assert.Equal(0x1f, encoded[2]); // CMOD_REQD
		int index = 3;
		Assert.Equal(typeCodedIndex(modifier), decompress(encoded, index, out index));
		Assert.Equal(0x08, encoded[index]);
	}

	[Fact]
	public static void OptionalModifiersUseTheirOwnElementType() {
		IlNamedTypeRef modifier = IlTest.Named("System.Runtime.CompilerServices", "CallConvCdecl", IlNamedTypeKind.Class);
		byte[] encoded = method(IlTest.Sig(IlReferenceFactory.OptionalModifier(modifier, IlTest.Int32)));

		Assert.Equal(0x20, encoded[2]); // CMOD_OPT
	}

	[Fact]
	public static void ModifiersPrecedeByRef() {
		IlNamedTypeRef modifier = IlTest.Named("System.Runtime.InteropServices", "InAttribute", IlNamedTypeKind.Class);
		IlTypeRef parameter = IlReferenceFactory.RequiredModifier(modifier, IlReferenceFactory.ByRef(IlTest.Int32));
		byte[] encoded = method(IlTest.Sig(IlTest.Void, parameter));

		// DEFAULT, 1 param, VOID, CMOD_REQD, <index>, BYREF, I4
		Assert.Equal(0x1f, encoded[3]);
		int index = 4;
		decompress(encoded, index, out index);
		Assert.Equal(0x10, encoded[index]);
		Assert.Equal(0x08, encoded[index + 1]);
	}

	[Fact]
	public static void ByRefIsRejectedInsideANestedType() =>
		// IlReferenceFactory.SzArray already rejects byref types, construct one manually to test the encoder's rejection
		Assert.Throws<IlEncodingException>(
			() => typeSpec(new IlSzArrayTypeRef(IlReferenceFactory.ByRef(IlTest.Int32)))
		);

	[Fact]
	public static void ByRefIsAcceptedAtASlot() =>
		Assert.Equal([0x00, 0x01, 0x01, 0x10, 0x08], method(IlTest.Sig(IlTest.Void, IlReferenceFactory.ByRef(IlTest.Int32))));

	[Fact]
	public static void PinnedIsAcceptedOnlyInALocalSignature() {
		IlPinnedTypeRef pinned = new(IlReferenceFactory.ByRef(IlTest.Int32));

		// LOCAL_SIG, 1 local, PINNED, BYREF, I4
		Assert.Equal(
			[0x07, 0x01, 0x45, 0x10, 0x08],
			SrmSignatureEncoder.EncodeLocalSignature([pinned], resolver).ToArray()
		);
		Assert.Throws<IlEncodingException>(() => method(IlTest.Sig(IlTest.Void, pinned)));
	}

	// ==========================================================================================
	// function pointers, locals, fields, method specs
	[Fact]
	public static void FunctionPointerEmbedsItsSignature() =>
		// FNPTR, then DEFAULT, 1 param, I4 return, I4 param
		Assert.Equal(
			[0x1b, 0x00, 0x01, 0x08, 0x08],
			typeSpec(IlReferenceFactory.FunctionPointer(IlTest.Sig(IlTest.Int32, IlTest.Int32)))
		);

	[Fact]
	public static void LocalSignatureCountsItsLocals() =>
		Assert.Equal(
			[0x07, 0x02, 0x08, 0x0e],
			SrmSignatureEncoder.EncodeLocalSignature([IlTest.Int32, IlTest.String], resolver).ToArray()
		);

	[Fact]
	public static void EmptyLocalSignatureIsRejected() =>
		Assert.Throws<IlEncodingException>(() => SrmSignatureEncoder.EncodeLocalSignature([], resolver));

	[Fact]
	public static void FieldSignatureUsesTheFieldHeader() =>
		Assert.Equal([0x06, 0x08], SrmSignatureEncoder.EncodeFieldSignature(IlTest.Int32, resolver).ToArray());

	[Fact]
	public static void MethodSpecificationListsItsArguments() =>
		Assert.Equal(
			[0x0a, 0x02, 0x08, 0x0e],
			SrmSignatureEncoder.EncodeMethodSpecification([IlTest.Int32, IlTest.String], resolver).ToArray()
		);

	[Fact]
	public static void EmptyMethodSpecificationIsRejected() =>
		Assert.Throws<IlEncodingException>(() => SrmSignatureEncoder.EncodeMethodSpecification([], resolver));

	// ==========================================================================================
	// compressed integers
	[Theory]
	[InlineData(0, new byte[] { 0x00 })]
	[InlineData(1, new byte[] { 0x02 })]
	[InlineData(-1, new byte[] { 0x7f })]
	[InlineData(3, new byte[] { 0x06 })]
	[InlineData(-3, new byte[] { 0x7b })]
	[InlineData(63, new byte[] { 0x7e })]
	[InlineData(-64, new byte[] { 0x01 })]
	[InlineData(64, new byte[] { 0x80, 0x80 })]
	[InlineData(-65, new byte[] { 0xbf, 0x7f })]
	[InlineData(8192, new byte[] { 0xc0, 0x00, 0x40, 0x00 })]
	[InlineData(-8192, new byte[] { 0x80, 0x01 })]
	[InlineData(268435455, new byte[] { 0xdf, 0xff, 0xff, 0xfe })]
	[InlineData(-268435456, new byte[] { 0xc0, 0x00, 0x00, 0x01 })]
	public static void ArrayLowerBoundsUseCompressedSignedIntegers(int lowerBound, byte[] expected) {
		IlArrayTypeRef array = IlReferenceFactory.Array(IlTest.Int32, rank: 1, sizes: [], lowerBounds: [lowerBound]);

		Assert.Equal([0x14, 0x08, 0x01, 0x00, 0x01, .. expected], typeSpec(array));
	}

	[Theory]
	[InlineData(0x00, new byte[] { 0x00 })]
	[InlineData(0x03, new byte[] { 0x03 })]
	[InlineData(0x7f, new byte[] { 0x7f })]
	[InlineData(0x80, new byte[] { 0x80, 0x80 })]
	[InlineData(0x2e57, new byte[] { 0xae, 0x57 })]
	[InlineData(0x3fff, new byte[] { 0xbf, 0xff })]
	[InlineData(0x4000, new byte[] { 0xc0, 0x00, 0x40, 0x00 })]
	[InlineData(0x1fffffff, new byte[] { 0xdf, 0xff, 0xff, 0xff })]
	public static void GenericParameterIndicesUseCompressedUnsignedIntegers(int index, byte[] expected) =>
		Assert.Equal([(byte)0x1e, .. expected], typeSpec(IlReferenceFactory.MethodGenericParameter(index)));

	private static int decompress(byte[] bytes, int offset, out int next) {
		byte first = bytes[offset];
		if ((first & 0x80) == 0) {
			next = offset + 1;
			return first;
		}
		if ((first & 0x40) == 0) {
			next = offset + 2;
			return ((first & 0x3f) << 8) | bytes[offset + 1];
		}
		next = offset + 4;
		return ((first & 0x1f) << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
	}
}
