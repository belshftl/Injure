// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Injure.Mods.Abstractions.Modif.Il.Metadata;

internal static class SrmMethodBodyDecoder {
	private const int noBranchTarget = -1;

	private readonly record struct PendingInstruction(
		int Offset,
		ILOpCode OpCode,
		IlOperand Operand,
		IlInstructionPrefixes? Prefixes,
		int BranchTargetOffset,
		int[]? SwitchTargetOffsets
	);

	private struct PrefixState {
		public int StartOffset;
		public IlPrefixFlags Flags;
		public IlTypeRef? ConstrainedType;
		public byte Alignment;
		public IlSkipChecks SkipChecks;

		public static PrefixState Empty => new() { StartOffset = -1 };
		public readonly bool IsPending => StartOffset >= 0;

		public readonly IlInstructionPrefixes? Build() =>
			Flags == IlPrefixFlags.None ? null : new IlInstructionPrefixes(Flags, ConstrainedType, Alignment, SkipChecks);
	}

	public static IlMethodBody Decode(
		PEReader peReader,
		MethodDefinitionHandle methodHandle,
		InternalIlProvenance baselineProvenance
	) {
		ArgumentNullException.ThrowIfNull(peReader);
		MetadataReader metadata = peReader.GetMetadataReader();
		MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
		if (method.RelativeVirtualAddress == 0)
			throw new ArgumentException("method has no IL body", nameof(methodHandle));
		return Decode(metadata, methodHandle, peReader.GetMethodBody(method.RelativeVirtualAddress), baselineProvenance);
	}

	public static IlMethodBody Decode(
		MetadataReader metadata,
		MethodDefinitionHandle methodHandle,
		BlobReader rawMethodBody,
		InternalIlProvenance baselineProvenance
	) => Decode(metadata, methodHandle, MethodBodyBlock.Create(rawMethodBody), baselineProvenance);

	public static IlMethodBody Decode(
		MetadataReader metadata,
		MethodDefinitionHandle methodHandle,
		MethodBodyBlock methodBody,
		InternalIlProvenance baselineProvenance
	) {
		SrmReferenceDecoder references = new(metadata);
		IlMethodRef method = references.ResolveMethod(methodHandle);
		ImmutableArray<IlTypeRef> locals = references.ResolveLocals(methodBody.LocalSignature);
		IlLocalSignatureOrigin localsOrigin = methodBody.LocalSignature.IsNil
			? default
			: new IlLocalSignatureOrigin(references.ModuleIdentity, MetadataTokens.GetRowNumber(methodBody.LocalSignature));

		List<PendingInstruction> pending = decodeInstructions(metadata, references, methodBody);
		int codeSize = methodBody.GetILReader().Length;

		List<IlAnchorId> anchors = new(pending.Count + 1);
		Dictionary<int, IlAnchorId> anchorsByOffset = new(pending.Count + 1);
		for (int i = 0; i < pending.Count; i++) {
			IlAnchorId anchor = new((ulong)(i + 1));
			anchors.Add(anchor);
			if (!anchorsByOffset.TryAdd(pending[i].Offset, anchor))
				throw new BadImageFormatException($"duplicate instruction offset IL_{pending[i].Offset:x4}");
		}
		IlAnchorId endAnchor = new((ulong)(pending.Count + 1));
		anchors.Add(endAnchor);
		if (!anchorsByOffset.TryAdd(codeSize, endAnchor))
			throw new BadImageFormatException($"end-of-body offset IL_{codeSize:x4} collides with an instruction offset");

		List<IlInstruction> instrs = new(pending.Count);
		for (int i = 0; i < pending.Count; i++) {
			PendingInstruction decoded = pending[i];
			IlOperand operand = decoded.Operand;
			if (decoded.BranchTargetOffset != noBranchTarget) {
				operand = new IlBranchOperand(getAnchor(anchorsByOffset, decoded.BranchTargetOffset, decoded.Offset));
			} else if (decoded.SwitchTargetOffsets is int[] targetOffsets) {
				ImmutableArray<IlAnchorId>.Builder targets = ImmutableArray.CreateBuilder<IlAnchorId>(targetOffsets.Length);
				foreach (int targetOffset in targetOffsets)
					targets.Add(getAnchor(anchorsByOffset, targetOffset, decoded.Offset));
				operand = new IlSwitchOperand(targets.MoveToImmutable());
			}
			instrs.Add(new IlInstruction(
				new IlInstructionId((ulong)(i + 1)),
				decoded.OpCode,
				operand,
				decoded.Prefixes,
				decoded.Offset,
				baselineProvenance
			));
		}

		List<IlExceptionRegion> exRegions = new(methodBody.ExceptionRegions.Length);
		foreach (ExceptionRegion region in methodBody.ExceptionRegions) {
			IlExceptionRegionKind kind = region.Kind switch {
				ExceptionRegionKind.Catch => IlExceptionRegionKind.Catch,
				ExceptionRegionKind.Filter => IlExceptionRegionKind.Filter,
				ExceptionRegionKind.Finally => IlExceptionRegionKind.Finally,
				ExceptionRegionKind.Fault => IlExceptionRegionKind.Fault,
				_ => throw new BadImageFormatException($"unknown exception region kind {region.Kind}"),
			};
			exRegions.Add(new IlExceptionRegion(
				kind,
				getAnchor(anchorsByOffset, region.TryOffset, region.TryOffset),
				getAnchor(anchorsByOffset, checked(region.TryOffset + region.TryLength), region.TryOffset),
				getAnchor(anchorsByOffset, region.HandlerOffset, region.HandlerOffset),
				getAnchor(anchorsByOffset, checked(region.HandlerOffset + region.HandlerLength), region.HandlerOffset),
				kind == IlExceptionRegionKind.Filter
					? getAnchor(anchorsByOffset, region.FilterOffset, region.FilterOffset)
					: null,
				kind == IlExceptionRegionKind.Catch
					? references.ResolveType(region.CatchType)
					: null
			));
		}

		return IlMethodBody.CreateDecoded(
			method,
			methodBody.LocalVariablesInitialized,
			locals,
			localsOrigin,
			instrs,
			anchors,
			exRegions
		);
	}

	private static List<PendingInstruction> decodeInstructions(
		MetadataReader metadata,
		SrmReferenceDecoder references,
		MethodBodyBlock methodBody
	) {
		BlobReader reader = methodBody.GetILReader();
		List<PendingInstruction> pending = new();
		PrefixState prefixes = PrefixState.Empty;

		while (reader.RemainingBytes != 0) {
			int offset = reader.Offset;
			ILOpCode encodedOpCode = readOpCode(ref reader, offset);
			IlOpCodeDescriptor descriptor = IlOpCodeInfo.GetDescriptor(encodedOpCode);
			if (!descriptor.IsDefined)
				throw new BadImageFormatException($"invalid IL opcode 0x{(int)encodedOpCode:x4} at IL_{offset:x4}");

			if (descriptor.Flow == IlFlowKind.Prefix) {
				readPrefix(ref reader, descriptor, references, ref prefixes, offset);
				continue;
			}

			IlOperand operand = IlNoneOperand.Instance;
			int branchTarget = noBranchTarget;
			int[]? switchTargets = null;

			switch (descriptor.Encoding) {
			case IlOperandEncoding.None:
				break;
			case IlOperandEncoding.Int8:
				operand = new IlInt32Operand(reader.ReadSByte());
				break;
			case IlOperandEncoding.UInt8:
				operand = new IlInt32Operand(reader.ReadByte());
				break;
			case IlOperandEncoding.Int32:
				operand = new IlInt32Operand(reader.ReadInt32());
				break;
			case IlOperandEncoding.Int64:
				operand = new IlInt64Operand(reader.ReadInt64());
				break;
			case IlOperandEncoding.Float32:
				operand = new IlFloat32Operand(reader.ReadSingle());
				break;
			case IlOperandEncoding.Float64:
				operand = new IlFloat64Operand(reader.ReadDouble());
				break;
			case IlOperandEncoding.Argument8:
				operand = new IlArgumentOperand(reader.ReadByte());
				break;
			case IlOperandEncoding.Argument16:
				operand = new IlArgumentOperand(reader.ReadUInt16());
				break;
			case IlOperandEncoding.Local8:
				operand = new IlLocalOperand(reader.ReadByte());
				break;
			case IlOperandEncoding.Local16:
				operand = new IlLocalOperand(reader.ReadUInt16());
				break;
			case IlOperandEncoding.Branch8:
				branchTarget = checked(reader.Offset + 1 + reader.ReadSByte());
				break;
			case IlOperandEncoding.Branch32:
				branchTarget = checked(reader.Offset + 4 + reader.ReadInt32());
				break;
			case IlOperandEncoding.Switch: {
				int count = reader.ReadInt32();
				if (count < 0 || count > reader.RemainingBytes / 4)
					throw new BadImageFormatException($"invalid switch target count {count} at IL_{offset:x4}");
				int[] deltas = new int[count];
				for (int i = 0; i < deltas.Length; i++)
					deltas[i] = reader.ReadInt32();
				int baseOffset = reader.Offset;
				switchTargets = new int[count];
				for (int i = 0; i < switchTargets.Length; i++)
					switchTargets[i] = checked(baseOffset + deltas[i]);
				break;
			}
			case IlOperandEncoding.StringToken:
				operand = new IlStringOperand(readUserString(metadata, ref reader));
				break;
			case IlOperandEncoding.TypeToken:
				operand = new IlTypeOperand(references.ResolveType(readEntityHandle(ref reader)));
				break;
			case IlOperandEncoding.MethodToken:
				operand = new IlMethodOperand(references.ResolveMethod(readEntityHandle(ref reader)));
				break;
			case IlOperandEncoding.FieldToken:
				operand = new IlFieldOperand(references.ResolveField(readEntityHandle(ref reader)));
				break;
			case IlOperandEncoding.SignatureToken: {
				EntityHandle handle = readEntityHandle(ref reader);
				if (handle.Kind != HandleKind.StandaloneSignature)
					throw new BadImageFormatException($"calli operand is {handle.Kind}, not StandaloneSignature");
				operand = new IlCallSiteOperand(references.ResolveCallSite((StandaloneSignatureHandle)handle));
				break;
			}
			case IlOperandEncoding.EntityToken:
				operand = resolveEntityOperand(references, readEntityHandle(ref reader));
				break;
			default:
				throw new BadImageFormatException($"unsupported operand encoding for {encodedOpCode}");
			}

			pending.Add(new PendingInstruction(
				prefixes.IsPending ? prefixes.StartOffset : offset,
				descriptor.Canonical,
				normalizeCompactOperand(encodedOpCode, operand),
				prefixes.Build(),
				branchTarget,
				switchTargets
			));
			prefixes = PrefixState.Empty;
		}

		if (prefixes.IsPending)
			throw new BadImageFormatException($"method body ends with an instruction prefix at IL_{prefixes.StartOffset:x4}");
		return pending;
	}

	private static void readPrefix(
		ref BlobReader reader,
		in IlOpCodeDescriptor descriptor,
		SrmReferenceDecoder references,
		ref PrefixState state,
		int offset
	) {
		IlPrefixFlags flag = descriptor.Prefix switch {
			IlPrefixKind.Constrained => IlPrefixFlags.Constrained,
			IlPrefixKind.Volatile => IlPrefixFlags.Volatile,
			IlPrefixKind.Tail => IlPrefixFlags.Tail,
			IlPrefixKind.Unaligned => IlPrefixFlags.Unaligned,
			IlPrefixKind.ReadOnly => IlPrefixFlags.ReadOnly,
			IlPrefixKind.No => IlPrefixFlags.No,
			_ => throw new InternalStateException($"prefix opcode has prefix kind '{descriptor.Prefix}'"),
		};
		if ((state.Flags & flag) != 0)
			throw new BadImageFormatException($"duplicate {descriptor.Prefix} prefix at IL_{offset:x4}");

		if (!state.IsPending)
			state.StartOffset = offset;
		state.Flags |= flag;

		switch (descriptor.Prefix) {
		case IlPrefixKind.Constrained:
			state.ConstrainedType = references.ResolveType(readEntityHandle(ref reader));
			break;
		case IlPrefixKind.Unaligned: {
			byte alignment = reader.ReadByte();
			if (alignment is not (1 or 2 or 4))
				throw new BadImageFormatException($"unaligned. alignment {alignment} at IL_{offset:x4} is not 1, 2, or 4");
			state.Alignment = alignment;
			break;
		}
		case IlPrefixKind.No: {
			byte checks = reader.ReadByte();
			if (checks == 0 || (checks & ~0x07) != 0)
				throw new BadImageFormatException($"no. operand 0x{checks:x2} at IL_{offset:x4} is not a valid check mask");
			state.SkipChecks = (IlSkipChecks)checks;
			break;
		}
		}
	}

	private static ILOpCode readOpCode(ref BlobReader reader, int offset) {
		byte first = reader.ReadByte();
		if (first != 0xfe)
			return (ILOpCode)first;
		if (reader.RemainingBytes == 0)
			throw new BadImageFormatException($"truncated two-byte opcode at IL_{offset:x4}");
		return (ILOpCode)(0xfe00 | reader.ReadByte());
	}

	private static string readUserString(MetadataReader metadata, ref BlobReader reader) {
		int token = reader.ReadInt32();
		if ((uint)token >> 24 != 0x70)
			throw new BadImageFormatException($"ldstr operand 0x{token:x8} is not a user-string token");
		return metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff));
	}

	private static EntityHandle readEntityHandle(ref BlobReader reader) => MetadataTokens.EntityHandle(reader.ReadInt32());

	private static IlOperand resolveEntityOperand(SrmReferenceDecoder references, EntityHandle handle) => handle.Kind switch {
		HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification =>
			new IlTypeOperand(references.ResolveType(handle)),
		HandleKind.MethodDefinition or HandleKind.MethodSpecification =>
			new IlMethodOperand(references.ResolveMethod(handle)),
		HandleKind.FieldDefinition => new IlFieldOperand(references.ResolveField(handle)),
		HandleKind.MemberReference => resolveMemberReferenceOperand(references, handle),
		_ => throw new BadImageFormatException($"ldtoken operand kind {handle.Kind} is not supported"),
	};

	private static IlOperand resolveMemberReferenceOperand(SrmReferenceDecoder references, EntityHandle handle) {
		try {
			return new IlMethodOperand(references.ResolveMethod(handle));
		} catch (BadImageFormatException) {
			return new IlFieldOperand(references.ResolveField(handle));
		}
	}

	private static IlOperand normalizeCompactOperand(ILOpCode encodedOpCode, IlOperand operand) => encodedOpCode switch {
		ILOpCode.Ldarg_0 => new IlArgumentOperand(0),
		ILOpCode.Ldarg_1 => new IlArgumentOperand(1),
		ILOpCode.Ldarg_2 => new IlArgumentOperand(2),
		ILOpCode.Ldarg_3 => new IlArgumentOperand(3),
		ILOpCode.Ldloc_0 => new IlLocalOperand(0),
		ILOpCode.Ldloc_1 => new IlLocalOperand(1),
		ILOpCode.Ldloc_2 => new IlLocalOperand(2),
		ILOpCode.Ldloc_3 => new IlLocalOperand(3),
		ILOpCode.Stloc_0 => new IlLocalOperand(0),
		ILOpCode.Stloc_1 => new IlLocalOperand(1),
		ILOpCode.Stloc_2 => new IlLocalOperand(2),
		ILOpCode.Stloc_3 => new IlLocalOperand(3),
		ILOpCode.Ldc_i4_m1 => new IlInt32Operand(-1),
		ILOpCode.Ldc_i4_0 => new IlInt32Operand(0),
		ILOpCode.Ldc_i4_1 => new IlInt32Operand(1),
		ILOpCode.Ldc_i4_2 => new IlInt32Operand(2),
		ILOpCode.Ldc_i4_3 => new IlInt32Operand(3),
		ILOpCode.Ldc_i4_4 => new IlInt32Operand(4),
		ILOpCode.Ldc_i4_5 => new IlInt32Operand(5),
		ILOpCode.Ldc_i4_6 => new IlInt32Operand(6),
		ILOpCode.Ldc_i4_7 => new IlInt32Operand(7),
		ILOpCode.Ldc_i4_8 => new IlInt32Operand(8),
		_ => operand,
	};

	private static IlAnchorId getAnchor(Dictionary<int, IlAnchorId> anchors, int offset, int sourceOffset) {
		if (!anchors.TryGetValue(offset, out IlAnchorId anchor))
			throw new BadImageFormatException($"IL_{sourceOffset:x4} references IL offset {offset:x4}, which is not an instruction boundary");
		return anchor;
	}
}
