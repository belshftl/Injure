// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Mods.Abstractions.Tests.MethodModification.Il.Metadata;

public sealed class SrmMethodBodyDecoderTests : IDisposable {
	private readonly FileStream stream;
	private readonly PEReader peReader;
	private readonly MetadataReader metadata;
	private readonly MethodDefinitionHandle anyMethod;
	private readonly int typeToken;

	public SrmMethodBodyDecoderTests() {
		string location = typeof(SrmMethodBodyDecoderTests).Assembly.Location;
		Assert.SkipWhen(string.IsNullOrEmpty(location), "test assembly has no on-disk location");
		stream = File.OpenRead(location);
		peReader = new PEReader(stream);
		metadata = peReader.GetMetadataReader();
		anyMethod = metadata.MethodDefinitions.First(h => metadata.GetMethodDefinition(h).RelativeVirtualAddress != 0);
		typeToken = MetadataTokens.GetToken(metadata.TypeDefinitions.First(h => !metadata.GetTypeDefinition(h).Name.IsNil));
	}

	public void Dispose() {
		peReader.Dispose();
		stream.Dispose();
	}

	private IlMethodBody decode(params byte[] il) {
		Assert.True(il.Length < 64, "manually written bodies must fit a tiny header");
		byte[] body = new byte[il.Length + 1];
		body[0] = (byte)((il.Length << 2) | 0x02);
		il.CopyTo(body, 1);
		unsafe {
			fixed (byte* p = body)
				return SrmMethodBodyDecoder.Decode(metadata, anyMethod, new BlobReader(p, body.Length), default);
		}
	}

	private static byte[] token(int value) => BitConverter.GetBytes(value);

	// ==========================================================================================
	// canonicalization
	[Theory]
	[InlineData(0x02, 0)] // ldarg.0
	[InlineData(0x05, 3)] // ldarg.3
	public void CompactArgFormsDecodeToCanonicalOpCode(byte opCode, int expectedIndex) {
		IlMethodBody body = decode(opCode, 0x26, 0x2a); // ldarg.n; pop; ret

		Assert.Equal(ILOpCode.Ldarg, body.Instructions[0].OpCode);
		Assert.Equal(expectedIndex, Assert.IsType<IlArgumentOperand>(body.Instructions[0].Operand).Index);
	}

	[Theory]
	[InlineData(new byte[] { 0x15 }, -1)] // ldc.i4.m1
	[InlineData(new byte[] { 0x16 }, 0)]
	[InlineData(new byte[] { 0x1e }, 8)] // ldc.i4.8
	[InlineData(new byte[] { 0x1f, 0xf6 }, -10)] // ldc.i4.s -10, signed
	public void CompactConstantFormsDecodeToCanonicalOpCode(byte[] encoded, int expectedValue) {
		IlMethodBody body = decode([.. encoded, 0x26, 0x2a]);

		Assert.Equal(ILOpCode.Ldc_i4, body.Instructions[0].OpCode);
		Assert.Equal(expectedValue, Assert.IsType<IlInt32Operand>(body.Instructions[0].Operand).Value);
	}

	[Fact]
	public void ShortBranchesDecodeToCanonicalOpCode() {
		IlMethodBody body = decode(0x2b, 0x00, 0x2a); // br.s +0; ret

		Assert.Equal(ILOpCode.Br, body.Instructions[0].OpCode);
		Assert.Equal(1, body.GetAnchorBoundary(Assert.IsType<IlBranchOperand>(body.Instructions[0].Operand).Target));
	}

	// ==========================================================================================
	// prefixes
	[Fact]
	public void PrefixesBundleOntoFollowingInstruction() {
		// volatile. ldsfld is awkward without a field token, and volatile. is legal before ldind too
		IlMethodBody body = decode(0x02, 0xfe, 0x13, 0x4a, 0x26, 0x2a); // ldarg.0; volatile.; ldind.i4; pop; ret

		Assert.Equal(4, body.Instructions.Count);
		IlInstruction prefixed = body.Instructions[1];
		Assert.Equal(ILOpCode.Ldind_i4, prefixed.OpCode);
		Assert.NotNull(prefixed.Prefixes);
		Assert.True(prefixed.Prefixes.Has(IlPrefixFlags.Volatile));
	}

	[Fact]
	public void BundleAnchorIsAtFirstPrefixByte() {
		IlMethodBody body = decode(0x02, 0xfe, 0x13, 0x4a, 0x26, 0x2a);

		// ldarg.0 occupies offset 0; the bundle starts at offset 1, not at the ldind at offset 3
		Assert.Equal(1, body.Instructions[1].OriginalOffset);
	}

	[Fact]
	public void MultiplePrefixesCombineOntoOneInstruction() {
		// unaligned. 4; volatile.; ldind.i4
		IlMethodBody body = decode(0x02, 0xfe, 0x12, 0x04, 0xfe, 0x13, 0x4a, 0x26, 0x2a);
		IlInstructionPrefixes prefixes = Assert.IsType<IlInstructionPrefixes>(body.Instructions[1].Prefixes);

		Assert.True(prefixes.Has(IlPrefixFlags.Unaligned));
		Assert.True(prefixes.Has(IlPrefixFlags.Volatile));
		Assert.Equal(4, prefixes.Alignment);
	}

	[Fact]
	public void ConstrainedCarriesItsTypeReference() {
		// constrained. <type>; ... using ldind.i4 as a stand-in target keeps the body decodable
		IlMethodBody body = decode([0x02, 0xfe, 0x16, .. token(typeToken), 0x4a, 0x26, 0x2a]);
		IlInstructionPrefixes prefixes = Assert.IsType<IlInstructionPrefixes>(body.Instructions[1].Prefixes);

		Assert.True(prefixes.Has(IlPrefixFlags.Constrained));
		Assert.NotNull(prefixes.ConstrainedType);
	}

	[Fact]
	public void NoPrefixDecodesCheckMask() {
		// no. 0x05 (typecheck | nullcheck); ldind.i4
		IlMethodBody body = decode(0x02, 0xfe, 0x19, 0x05, 0x4a, 0x26, 0x2a);
		IlInstructionPrefixes prefixes = Assert.IsType<IlInstructionPrefixes>(body.Instructions[1].Prefixes);

		Assert.Equal(IlSkipChecks.TypeCheck | IlSkipChecks.NullCheck, prefixes.SkipChecks);
	}

	[Fact]
	public void BodyEndingInPrefixIsRejected() =>
		Assert.Throws<BadImageFormatException>(() => decode(0x2a, 0xfe, 0x13)); // ret; volatile.

	[Fact]
	public void DuplicatedPrefixIsRejected() =>
		Assert.Throws<BadImageFormatException>(() => decode(0x02, 0xfe, 0x13, 0xfe, 0x13, 0x4a, 0x26, 0x2a));

	[Theory]
	[InlineData(0x00)]
	[InlineData(0x03)]
	[InlineData(0x08)]
	public void InvalidUnalignedAlignmentIsRejected(byte alignment) =>
		Assert.Throws<BadImageFormatException>(() => decode(0x02, 0xfe, 0x12, alignment, 0x4a, 0x26, 0x2a));

	[Theory]
	[InlineData(0x00)]
	[InlineData(0x08)]
	[InlineData(0xff)]
	public void InvalidNoCheckMaskIsRejected(byte mask) =>
		Assert.Throws<BadImageFormatException>(() => decode(0x02, 0xfe, 0x19, mask, 0x4a, 0x26, 0x2a));

	[Fact]
	public void BranchIntoPrefixBundleIsRejected() {
		// br.s +1 lands between the volatile. prefix and its ldind.i4
		Assert.Throws<BadImageFormatException>(() => decode(0x02, 0x2b, 0x01, 0xfe, 0x13, 0x4a, 0x26, 0x2a));
	}

	// ==========================================================================================
	// malformed bodies
	[Fact]
	public void UndefinedOpCodeIsRejected() =>
		Assert.Throws<BadImageFormatException>(() => decode(0xfe, 0x1f, 0x2a));

	[Fact]
	public void BranchPastBodyEndIsRejected() =>
		Assert.Throws<BadImageFormatException>(() => decode(0x2b, 0x7f, 0x2a));

	[Fact]
	public void TruncatedTwoByteOpCodeIsRejected() =>
		Assert.Throws<BadImageFormatException>(() => decode(0x2a, 0xfe));

	[Fact]
	public void BranchToEndBoundaryIsAccepted() {
		// ret; br.s -> end of body
		IlMethodBody body = decode(0x2a, 0x2b, 0x00);

		Assert.Equal(2, body.GetAnchorBoundary(Assert.IsType<IlBranchOperand>(body.Instructions[1].Operand).Target));
	}

	[Fact]
	public void SwitchTargetsAreRelativeToEndOfInstruction() {
		// ldc.i4.0; switch 2 -> +0, +1; nop; nop; ret
		IlMethodBody body = decode(
			0x16,
			0x45, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x2a
		);
		IlSwitchOperand @switch = Assert.IsType<IlSwitchOperand>(body.Instructions[1].Operand);

		Assert.Equal(2, @switch.Targets.Length);
		Assert.Equal(2, body.GetAnchorBoundary(@switch.Targets[0]));
		Assert.Equal(3, body.GetAnchorBoundary(@switch.Targets[1]));
	}

	// ==========================================================================================
	// integration with matching
	[Fact]
	public void DecodedShortFormsMatchLongFormPatterns() {
		IlMethodBody body = decode(0x16, 0x26, 0x2a); // ldc.i4.0; pop; ret
		IlTransactionCore core = new(body, IlTest.OwnerId, IlTest.LocalId, default, null);

		Assert.Equal(1, core.MatchAll([MatchIl.LdcI4(0), MatchIl.Pop], IlPatternProvenanceConstraint.Any).Count);
	}
}
