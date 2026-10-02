// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Tests.Modif.Il.Metadata;

public sealed class SrmMethodBodyEncoderTests {
	private static IlEncodedMethodBody encode(IlMethodBody body, in IlEncodingOptions options = default) =>
		SrmMethodBodyEncoder.Prepare(body, new FakeTokenResolver(), options);

	private static ReadOnlySpan<byte> code(IlEncodedMethodBody encoded) =>
		encoded.AsSpan().Slice(encoded.HeaderSize, encoded.CodeSize);

	// ==========================================================================================
	// headers
	[Fact]
	public static void TinyHeaderIsUsedWhenEverythingFits() {
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Ret().Build());

		Assert.Equal(1, encoded.HeaderSize);
		Assert.Equal(1, encoded.CodeSize);
		Assert.Equal(2, encoded.Size);
		Assert.Equal((1 << 2) | 0x02, encoded.AsSpan()[0]);
	}

	[Theory]
	[InlineData(62, 1)] // 62 nops + ret = 63 bytes, fits into tiny
	[InlineData(63, 12)] // 64 bytes, no longer tiny
	public static void TinyHeaderStopsAt64CodeBytes(int nops, int expectedHeaderSize) {
		BodyBuilder builder = new();
		for (int i = 0; i < nops; i++)
			builder.Nop();
		IlEncodedMethodBody encoded = encode(builder.Ret().Build());

		Assert.Equal(nops + 1, encoded.CodeSize);
		Assert.Equal(expectedHeaderSize, encoded.HeaderSize);
	}

	[Theory]
	[InlineData(8, 1)]
	[InlineData(9, 12)]
	public static void TinyHeaderStopsAbove8MaxStack(int depth, int expectedHeaderSize) {
		BodyBuilder builder = new();
		for (int i = 0; i < depth; i++)
			builder.LdcI4(0);
		for (int i = 0; i < depth; i++)
			builder.Pop();
		IlEncodedMethodBody encoded = encode(builder.Ret().Build());

		Assert.Equal(depth, encoded.MaxStack);
		Assert.Equal(expectedHeaderSize, encoded.HeaderSize);
	}

	[Fact]
	public static void LocalsForceFatHeader() {
		ImmutableArray<IlTypeRef> locals = [IlTest.Int32];
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Ret().Build(locals: locals));

		Assert.Equal(12, encoded.HeaderSize);
		Assert.Equal(0x10, BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan()) & 0x10); // InitLocals
	}

	[Fact]
	public static void InitLocalsIsClearWhenRequested() {
		ImmutableArray<IlTypeRef> locals = [IlTest.Int32];
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Ret().Build(locals: locals, initLocals: false));

		Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan()) & 0x10);
	}

	[Fact]
	public static void InitLocalsIsMeaninglessWithoutLocals() {
		IlMethodBody body = new BodyBuilder().Ret().Build(initLocals: true);
		Assert.False(body.InitLocals);
		Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(
			encode(body, new IlEncodingOptions { ForceFatHeader = true }).AsSpan()
		) & 0x10);
	}

	[Fact]
	public static void FatHeaderCarriesMaxStackCodeSizeAndLocalsToken() {
		ImmutableArray<IlTypeRef> locals = [IlTest.Int32];
		IlMethodBody body = new BodyBuilder().LdcI4(1).Pop().Ret().Build(locals: locals);
		FakeTokenResolver resolver = new();
		IlEncodedMethodBody encoded = SrmMethodBodyEncoder.Prepare(body, resolver);
		ReadOnlySpan<byte> header = encoded.AsSpan();

		Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(header) >> 12); // header size in dwords
		Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(header[2..]));
		Assert.Equal(encoded.CodeSize, BinaryPrimitives.ReadInt32LittleEndian(header[4..]));
		Assert.Equal(
			MetadataTokens.GetToken(resolver.SynthesizedLocals),
			BinaryPrimitives.ReadInt32LittleEndian(header[8..])
		);
	}

	[Fact]
	public static void ForceFatHeaderOverridesEligibility() {
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Ret().Build(), new IlEncodingOptions { ForceFatHeader = true });

		Assert.Equal(12, encoded.HeaderSize);
	}

	[Fact]
	public static void NoLocalsMeansNoLocalsTokenResolution() {
		FakeTokenResolver resolver = new();
		SrmMethodBodyEncoder.Prepare(
			new BodyBuilder().Ret().Build(),
			resolver,
			new IlEncodingOptions { ForceFatHeader = true }
		);

		Assert.Equal(0, resolver.LocalsOriginHits + resolver.LocalsOriginMisses);
	}

	[Fact]
	public static void LocalsOriginHintIsPassedThrough() {
		ImmutableArray<IlTypeRef> locals = [IlTest.Int32];
		IlLocalSignatureOrigin origin = new(new IlModuleIdentity(Guid.Empty, "Test"), 7);
		IlMethodBody body = new BodyBuilder().Ret().Build(locals: locals, localsOrigin: origin);
		FakeTokenResolver resolver = new();
		IlEncodedMethodBody encoded = SrmMethodBodyEncoder.Prepare(body, resolver);

		Assert.Equal(1, resolver.LocalsOriginHits);
		Assert.Equal(0, resolver.LocalsOriginMisses);
		Assert.Equal(
			MetadataTokens.GetToken(origin.Handle),
			BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan()[8..])
		);
	}

	// ==========================================================================================
	// compact forms
	[Theory]
	[InlineData(0, new byte[] { 0x02 })] // ldarg.0
	[InlineData(3, new byte[] { 0x05 })] // ldarg.3
	[InlineData(4, new byte[] { 0x0e, 0x04 })] // ldarg.s
	[InlineData(255, new byte[] { 0x0e, 0xff })]
	[InlineData(256, new byte[] { 0xfe, 0x09, 0x00, 0x01 })] // ldarg
	public static void ArgFormsUseShortestEncoding(int index, byte[] expected) {
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Ldarg(index).Pop().Ret().Build());

		Assert.Equal(expected, code(encoded)[..expected.Length].ToArray());
	}

	[Theory]
	[InlineData(-1, new byte[] { 0x15 })] // ldc.i4.m1
	[InlineData(0, new byte[] { 0x16 })]
	[InlineData(8, new byte[] { 0x1e })] // ldc.i4.8
	[InlineData(9, new byte[] { 0x1f, 0x09 })] // ldc.i4.s
	[InlineData(127, new byte[] { 0x1f, 0x7f })]
	[InlineData(-128, new byte[] { 0x1f, 0x80 })]
	[InlineData(128, new byte[] { 0x20, 0x80, 0x00, 0x00, 0x00 })] // ldc.i4
	public static void ConstantFormsUseShortestEncoding(int value, byte[] expected) {
		IlEncodedMethodBody encoded = encode(new BodyBuilder().LdcI4(value).Pop().Ret().Build());

		Assert.Equal(expected, code(encoded)[..expected.Length].ToArray());
	}

	[Fact]
	public static void CompactFormsCanBeDisabled() {
		IlEncodedMethodBody encoded = encode(
			new BodyBuilder().LdcI4(0).Pop().Ret().Build(),
			new IlEncodingOptions { ForceCanonicalForm = true }
		);

		Assert.Equal(new byte[] { 0x20, 0x00, 0x00, 0x00, 0x00 }, code(encoded)[..5].ToArray());
	}

	// ==========================================================================================
	// branches and switch
	private const byte brS = 0x2b;
	private const byte br = 0x38;

	private static BodyBuilder nops(BodyBuilder builder, int count) {
		for (int i = 0; i < count; i++)
			builder.Nop();
		return builder;
	}

	[Fact]
	public static void ANearBranchUsesTheShortForm() {
		// br.s -> 1; ret
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Br(1).Ret().Build());

		Assert.Equal(new byte[] { brS, 0x00, 0x2a }, code(encoded).ToArray());
	}

	[Fact]
	public static void AConditionalBranchUsesItsShortForm() {
		// ldc.i4.0; brtrue.s -> 3; nop; ret
		IlEncodedMethodBody encoded = encode(new BodyBuilder().LdcI4(0).Brtrue(3).Nop().Ret().Build());

		Assert.Equal(new byte[] { 0x16, 0x2d, 0x01, 0x00, 0x2a }, code(encoded).ToArray());
	}

	[Fact]
	public static void BranchToTheEndOfTheCodeIsRejected() =>
		// ret; br -> end
		// CoreCLR rejects this even when unreachable, and it has no position operand in bounds
		Assert.Throws<IlEncodingException>(static () => encode(new BodyBuilder().Ret().Br(2).Build()));

	[Fact]
	public static void SwitchTargetAtTheEndOfTheCodeIsRejected() =>
		// ldc.i4.0; switch -> 2, end; ret
		Assert.Throws<IlEncodingException>(static () => encode(new BodyBuilder().LdcI4(0).Switch(2, 3).Ret().Build()));

	[Theory]
	[InlineData(127, false)]
	[InlineData(128, true)]
	public static void AForwardBranchIsShortUpTo127Bytes(int gap, bool expectLong) {
		// br -> past the nops; nop * gap; ret
		IlEncodedMethodBody encoded = encode(nops(new BodyBuilder().Br(1 + gap), gap).Ret().Build());
		ReadOnlySpan<byte> bytes = code(encoded);

		if (expectLong) {
			Assert.Equal(br, bytes[0]);
			Assert.Equal(gap, BinaryPrimitives.ReadInt32LittleEndian(bytes[1..]));
		} else {
			Assert.Equal(brS, bytes[0]);
			Assert.Equal(gap, (sbyte)bytes[1]);
		}
	}

	[Theory]
	[InlineData(126, false)]
	[InlineData(127, true)]
	public static void ABackwardBranchIsShortDownToMinus128Bytes(int gap, bool expectLong) {
		// nop * gap; br -> 0; ret
		IlEncodedMethodBody encoded = encode(nops(new BodyBuilder(), gap).Br(0).Ret().Build());
		ReadOnlySpan<byte> bytes = code(encoded)[gap..];

		if (expectLong) {
			Assert.Equal(br, bytes[0]);
			Assert.Equal(-(gap + 5), BinaryPrimitives.ReadInt32LittleEndian(bytes[1..]));
		} else {
			Assert.Equal(brS, bytes[0]);
			Assert.Equal(-(gap + 2), (sbyte)bytes[1]);
		}
	}

	[Fact]
	public static void LengtheningOneBranchCanPushAnotherOutOfRange() {
		// 0: br -> 126; 1: br -> 326; nop * 324; 326: ret
		// the first branch spans the second plus 124 nops: 126 bytes with the second short, 129 with it
		// long, and the second has to be long
		IlEncodedMethodBody encoded = encode(nops(new BodyBuilder().Br(126).Br(326), 324).Ret().Build());
		ReadOnlySpan<byte> bytes = code(encoded);

		Assert.Equal(br, bytes[0]);
		Assert.Equal(129, BinaryPrimitives.ReadInt32LittleEndian(bytes[1..]));
		Assert.Equal(br, bytes[5]);
	}

	[Fact]
	public static void BranchesThatOnlyFitTogetherAreBothShort() {
		// 0: br -> 126; nop * 124; 125: br -> 0; 126: ret
		// each fits only while the other is short, which starting from long forms would never find
		IlEncodedMethodBody encoded = encode(nops(new BodyBuilder().Br(126), 124).Br(0).Ret().Build());
		ReadOnlySpan<byte> bytes = code(encoded);

		Assert.Equal(129, encoded.CodeSize);
		Assert.Equal(new byte[] { brS, 126 }, bytes[..2].ToArray());
		Assert.Equal(new byte[] { brS, unchecked((byte)-128) }, bytes[126..128].ToArray());
	}

	[Fact]
	public static void ShortBranchesCanBeDisabled() {
		IlEncodedMethodBody encoded = encode(
			new BodyBuilder().Br(1).Ret().Build(),
			new IlEncodingOptions { ForceCanonicalForm = true }
		);

		Assert.Equal(new byte[] { br, 0x00, 0x00, 0x00, 0x00, 0x2a }, code(encoded).ToArray());
	}

	[Fact]
	public static void ExceptionClausesUseTheFinalLayout() {
		// 0: try { leave -> 202 } 1: finally { endfinally } nop * 200; 202: ret
		// the leave spans 201 bytes, so it's long, which moves the handler from offset 2 to 5
		BodyBuilder builder = nops(new BodyBuilder().Leave(202).Add(ILOpCode.Endfinally), 200).Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, 0, 1, 1, 2);
		IlEncodedMethodBody encoded = encode(builder.Build());
		int codeEnd = encoded.HeaderSize + encoded.CodeSize;
		ReadOnlySpan<byte> clause = encoded.AsSpan()[(codeEnd + (4 - codeEnd % 4) % 4 + 4)..];

		Assert.Equal(0x01, encoded.AsSpan()[codeEnd + (4 - codeEnd % 4) % 4]); // small section
		Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(clause[2..])); // try offset
		Assert.Equal(5, clause[4]); // try length
		Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(clause[5..])); // handler offset
		Assert.Equal(1, clause[7]); // handler length
	}

	[Fact]
	public static void SwitchDeltasAreRelativeToEndOfInstruction() {
		// ldc.i4.0; switch -> 3, 4; nop; nop; ret
		IlMethodBody body = new BodyBuilder().LdcI4(0).Switch(3, 4).Nop().Nop().Ret().Build();
		ReadOnlySpan<byte> encoded = code(encode(body));

		// 1 byte ldc.i4.0, then switch: 1 opcode + 4 count + 8 deltas = 13; instruction ends at 14
		Assert.Equal(0x45, encoded[1]);
		Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(encoded[2..]));
		Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(encoded[6..])); // boundary 3 is at offset 15
		Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(encoded[10..])); // boundary 4 is at offset 16
	}

	// ==========================================================================================
	// exception sections
	private static IlMethodBody withClauses(int count) {
		// try { leave -> end } finally { endfinally }
		// repeated, then ret
		BodyBuilder builder = new();
		int end = 3 * count;
		for (int i = 0; i < count; i++)
			builder.Leave(end).Add(ILOpCode.Endfinally).Nop();
		builder.Ret();
		for (int i = 0; i < count; i++)
			builder.ExceptionRegion(IlExceptionRegionKind.Finally, 3 * i, 3 * i + 1, 3 * i + 1, 3 * i + 2);
		return builder.Build();
	}

	[Theory]
	[InlineData(1, 12)]
	[InlineData(20, 12)]
	[InlineData(21, 24)]
	public static void ExceptionSectionFormatFollowsClauseCount(int clauses, int expectedClauseSize) {
		IlEncodedMethodBody encoded = encode(withClauses(clauses));
		int codeEnd = encoded.HeaderSize + encoded.CodeSize;
		int sectionStart = codeEnd + (4 - codeEnd % 4) % 4;
		ReadOnlySpan<byte> section = encoded.AsSpan()[sectionStart..];

		bool fat = expectedClauseSize == 24;
		Assert.Equal(fat ? 0x41 : 0x01, section[0]);
		Assert.Equal(4 + expectedClauseSize * clauses, encoded.Size - sectionStart);
	}

	[Fact]
	public static void ForceFatExceptionSectionsOverridesEligibility() {
		IlEncodedMethodBody encoded = encode(withClauses(1), new IlEncodingOptions { ForceFatExceptionSections = true });
		int codeEnd = encoded.HeaderSize + encoded.CodeSize;
		int sectionStart = codeEnd + (4 - codeEnd % 4) % 4;

		Assert.Equal(0x41, encoded.AsSpan()[sectionStart]);
		Assert.Equal(4 + 24, encoded.Size - sectionStart);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public static void ExceptionSectionIs4ByteAligned(int padding) {
		BodyBuilder builder = new();
		int nops = (4 - padding) % 4;
		for (int i = 0; i < nops; i++)
			builder.Nop();
		int leaveTarget = nops + 3;
		builder.Leave(leaveTarget).Add(ILOpCode.Endfinally).Nop().Ret();
		builder.ExceptionRegion(IlExceptionRegionKind.Finally, nops, nops + 1, nops + 1, nops + 2);
		IlEncodedMethodBody encoded = encode(builder.Build());

		int codeEnd = encoded.HeaderSize + encoded.CodeSize;
		int sectionStart = codeEnd + (4 - codeEnd % 4) % 4;
		Assert.Equal(0, sectionStart % 4);
		Assert.True(encoded.Size > sectionStart, "no room for an exception section");
	}

	[Fact]
	public static void ExceptionSectionsSetTheMoreSectsFlag() {
		IlEncodedMethodBody encoded = encode(withClauses(1));

		Assert.Equal(0x08, BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan()) & 0x08);
	}

	// ==========================================================================================
	// output handling
	[Fact]
	public static void WriteToRejectsTooSmallDestination() {
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Ret().Build());

		Assert.Throws<ArgumentException>(() => encoded.WriteTo(new byte[encoded.Size - 1]));
	}

	[Fact]
	public static void WriteToFillsExactlyTheEncodedBody() {
		IlEncodedMethodBody encoded = encode(new BodyBuilder().LdcI4(5).Pop().Ret().Build());
		byte[] destination = new byte[encoded.Size + 4];
		encoded.WriteTo(destination);

		Assert.Equal(encoded.ToArray(), destination[..encoded.Size]);
		Assert.Equal(new byte[4], destination[encoded.Size..]);
	}

	[Fact]
	public static void EncodingFailureSurfacesAsAnEncodingException() {
		IlMethodBody body = new BodyBuilder()
			.Add(ILOpCode.Castclass, new IlTypeOperand(IlTest.Object))
			.Pop()
			.Ret()
			.Build(IlTest.Sig(IlTest.Void, IlTest.Object));

		// the resolver runs out before the type is resolved
		Assert.ThrowsAny<Exception>(() => SrmMethodBodyEncoder.Prepare(body, new FailingTokenResolver(0)));
	}

	[Fact]
	public static void InvalidBodiesAreRejectedBeforeEncoding() {
		IlMethodBody body = new BodyBuilder().Pop().Ret().Build();

		Assert.Throws<IlInvalidMethodException>(() => encode(body));
	}

	// ==========================================================================================
	// declared locals
	private static IlMethodBody withDeclaredLocal(IlMethodBody body) {
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, null);
		IlLocal local = core.DeclareLocal(IlTest.Int32);
		core.EmitAtBoundary(0, e => { e.LdcI4(0); e.Stloc(local); });
		core.Commit();
		return body;
	}

	[Fact]
	public static void DeclaringOnALocallessBodyForcesAFatHeaderWithInitlocals() {
		IlEncodedMethodBody encoded = encode(withDeclaredLocal(new BodyBuilder().Ret().Build()));

		Assert.Equal(12, encoded.HeaderSize);
		Assert.Equal(0x10, BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan()) & 0x10);
	}

	[Fact]
	public static void DeclaredLocalsAreNotResolvedThroughTheOriginalSignature() {
		// the original StandAloneSig has one local too few, so reusing it would produce invalid cil
		IlLocalSignatureOrigin origin = new(new IlModuleIdentity(Guid.Empty, "Test"), 7);
		IlMethodBody body = withDeclaredLocal(new BodyBuilder().Ret().Build(locals: [IlTest.Int32], localsOrigin: origin));
		FakeTokenResolver resolver = new();
		IlEncodedMethodBody encoded = SrmMethodBodyEncoder.Prepare(body, resolver);

		Assert.Equal(0, resolver.LocalsOriginHits);
		Assert.Equal(1, resolver.LocalsOriginMisses);
		Assert.Equal(
			MetadataTokens.GetToken(resolver.SynthesizedLocals),
			BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan()[8..])
		);
	}

	[Theory]
	[InlineData(0, new byte[] { 0x0a })] // stloc.0
	[InlineData(3, new byte[] { 0x0d })] // stloc.3
	[InlineData(4, new byte[] { 0x13, 0x04 })] // stloc.s
	[InlineData(255, new byte[] { 0x13, 0xff })]
	[InlineData(256, new byte[] { 0xfe, 0x0e, 0x00, 0x01 })] // stloc
	public static void DeclaredLocalIndexUsesShortestEncoding(int existing, byte[] expected) {
		var locals = Enumerable.Repeat<IlTypeRef>(IlTest.Int32, existing).ToImmutableArray();
		IlEncodedMethodBody encoded = encode(withDeclaredLocal(new BodyBuilder().Ret().Build(locals: locals)));

		// skip the ldc.i4.0
		Assert.Equal(expected, code(encoded)[1..(1 + expected.Length)].ToArray());
	}

	// ==========================================================================================
	// float roundtrip
	[Theory]
	[InlineData(0x7fc00001u)] // quiet NaN, nonzero payload
	[InlineData(0x7f800001u)] // signaling NaN
	[InlineData(0x80000000u)] // -0.0
	public static void Float32OperandsKeepTheirBitPattern(uint bits) {
		IlMethodBody body = new BodyBuilder()
			.Add(ILOpCode.Ldc_r4, new IlFloat32Operand(BitConverter.UInt32BitsToSingle(bits)))
			.Pop()
			.Ret()
			.Build();

		Assert.Equal(bits, BinaryPrimitives.ReadUInt32LittleEndian(code(encode(body))[1..]));
	}
}
