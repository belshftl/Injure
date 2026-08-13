// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Mods.Abstractions.Tests.MethodModification.Il.Metadata;

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
			new IlEncodingOptions { DisableCompactForms = true }
		);

		Assert.Equal(new byte[] { 0x20, 0x00, 0x00, 0x00, 0x00 }, code(encoded)[..5].ToArray());
	}

	// ==========================================================================================
	// branches and switch
	[Fact]
	public static void BranchesAreCurrentlyAlwaysLongForm() {
		// br -> 1; ret
		// the branch is 5 bytes and jumps zero bytes forward
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Br(1).Ret().Build());

		Assert.Equal(6, encoded.CodeSize);
		Assert.Equal(0x38, code(encoded)[0]);
		Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(code(encoded)[1..]));
	}

	[Fact]
	public static void BranchToTheEndBoundaryIsEncodable() {
		// ret; br -> end
		// unreachable, but the target is the end-of-body anchor
		IlEncodedMethodBody encoded = encode(new BodyBuilder().Ret().Br(2).Build());

		Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(code(encoded)[2..]));
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
}
