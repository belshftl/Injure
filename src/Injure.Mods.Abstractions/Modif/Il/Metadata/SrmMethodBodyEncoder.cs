// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Injure.Mods.Abstractions.Modif.Il.Metadata;

/// <summary>
/// Method body encoding policy.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is the default / recommended settings; the flags primarily exist
/// for debugging/testing purposes.
/// </remarks>
internal readonly struct IlEncodingOptions {
	/// <summary>
	/// Emits long forms of <c>ldarg</c>/<c>ldloc</c>/<c>ldc.i4</c> and friends even when a compact
	/// form would do.
	/// </summary>
	public bool DisableCompactForms { get; init; }

	/// <summary>
	/// Emits a fat method header even when a tiny header would do. For tests.
	/// </summary>
	public bool ForceFatHeader { get; init; }

	/// <summary>
	/// Emits a fat exception-handling section even when a small section would do. For tests.
	/// </summary>
	public bool ForceFatExceptionSections { get; init; }
}

/// <summary>
/// A finished CLR method body ready to be copied into CLR memory by a profiler.
/// </summary>
/// <remarks>
/// All token resolution, layout, and byte generation happen in
/// <see cref="SrmMethodBodyEncoder.Prepare"/>, so <see cref="WriteTo"/> is a copy. The intent is that
/// a profiler callbakc allocates with <c>IMethodMalloc</c> or receives a ReJIT buffer and fills it
/// without having to do any complex work that can block or fail.
/// </remarks>
internal sealed class IlEncodedMethodBody {
	private readonly byte[] bytes;

	/// <summary>
	/// Total method body size in bytes, including header and exception sections.
	/// </summary>
	public int Size => bytes.Length;

	/// <summary>
	/// Size of the method header in bytes (1 for tiny, 12 for fat).
	/// </summary>
	public int HeaderSize { get; }

	/// <summary>
	/// Size of the IL code portion in bytes.
	/// </summary>
	public int CodeSize { get; }

	/// <summary>
	/// Computed max stack height.
	/// </summary>
	public int MaxStack { get; }

	internal IlEncodedMethodBody(byte[] bytes, int headerSize, int codeSize, int maxStack) {
		InternalStateException.ThrowIfNull(bytes);
		this.bytes = bytes;
		HeaderSize = headerSize;
		CodeSize = codeSize;
		MaxStack = maxStack;
	}

	/// <summary>
	/// Copies the encoded body into <paramref name="dst"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="dst"/> does not have at least <see cref="Size"/> writable bytes.
	/// </exception>
	public void WriteTo(Span<byte> dst) {
		if (dst.Length < bytes.Length)
			throw new ArgumentException(
				$"expected at least {bytes.Length} bytes at destination, got {dst.Length}",
				nameof(dst)
			);
		bytes.CopyTo(dst);
	}

	/// <summary>
	/// The encoded body. Valid for the lifetime of this instance.
	/// </summary>
	public ReadOnlySpan<byte> AsSpan() => bytes;

	/// <summary>
	/// Copies the encoded body into a new array. For tests and diagnostics; prefer
	/// <see cref="WriteTo"/> on runtime paths.
	/// </summary>
	public byte[] ToArray() => bytes.AsSpan().ToArray();
}

/// <summary>
/// Encodes a semantic method body into a complete CLR method body.
/// </summary>
/// <remarks>
/// <para>
/// Branches are always emitted in their long form, so instruction sizes are known without iterating
/// to a fixed point and layout is a single pass. Non-branch compact forms are offset-independent and
/// are selected by default. Whole-method branch relaxation remains deferred.
/// </para>
/// <para>
/// Prefixes are emitted in fixed order, innermost last: <c>no.</c>, <c>readonly.</c>,
/// <c>unaligned.</c>, <c>volatile.</c>, <c>tail.</c>, <c>constrained.</c>. This keeps
/// <c>constrained.</c> and <c>tail.</c> adjacent to the instruction they qualify, as ECMA-335
/// requires. Decoded prefix order is therefore not preserved.
/// </para>
/// </remarks>
internal static class SrmMethodBodyEncoder {
	private const int tinyFormat = 0x02;
	private const int fatFormat = 0x03;
	private const int moreSects = 0x08;
	private const int initLocalsFlag = 0x10;
	private const int fatHeaderSize = 12;
	private const int tinyHeaderSize = 1;
	private const int maxTinyCodeSize = 64;
	private const int maxTinyMaxStack = 8;

	private const int ehTable = 0x01;
	private const int ehFatFormat = 0x40;
	private const int smallClauseSize = 12;
	private const int fatClauseSize = 24;
	private const int ehSectionHeaderSize = 4;
	private const int maxSmallClauses = 20;

	private const int clauseException = 0x0000;
	private const int clauseFilter = 0x0001;
	private const int clauseFinally = 0x0002;
	private const int clauseFault = 0x0004;

	private readonly record struct EmittedForm(ILOpCode OpCode, int OperandSize);

	/// <summary>
	/// Resolves, lays out, and encodes a method body.
	/// </summary>
	/// <exception cref="IlInvalidMethodException">Thrown if the body fails stack analysis.</exception>
	/// <exception cref="IlEncodingException">Thrown if the body cannot be encoded.</exception>
	public static IlEncodedMethodBody Prepare(
		IlMethodBody body,
		IIlTokenResolver resolver,
		in IlEncodingOptions options = default
	) {
		InternalStateException.ThrowIfNull(body);
		ArgumentNullException.ThrowIfNull(resolver);

		int maxStack = IlMaxStackAnalyzer.Analyze(body);
		IReadOnlyList<IlInstruction> instrs = body.Instructions;
		int count = instrs.Count;

		int[] operandTokens = new int[count];
		int[] prefixTokens = new int[count];
		resolveTokens(instrs, resolver, operandTokens, prefixTokens);

		var forms = new EmittedForm[count];
		int[] offsets = new int[count + 1];
		bool compact = !options.DisableCompactForms;
		int offset = 0;
		for (int i = 0; i < count; i++) {
			offsets[i] = offset;
			IlInstruction instr = instrs[i];
			EmittedForm form = selectForm(instr, compact);
			forms[i] = form;
			offset = checked(offset + prefixSize(instr.Prefixes) + IlOpCodeInfo.GetOpCodeSize(form.OpCode) + form.OperandSize);
		}
		offsets[count] = offset;
		int codeSize = offset;

		IReadOnlyList<IlExceptionRegion> regions = body.ExceptionRegions;
		bool hasExceptionRegions = regions.Count > 0;
		bool tinyHeader =
			!options.ForceFatHeader &&
			!hasExceptionRegions &&
			body.Locals.IsEmpty &&
			maxStack <= maxTinyMaxStack &&
			codeSize < maxTinyCodeSize;
		int headerSize = tinyHeader ? tinyHeaderSize : fatHeaderSize;

		int codeEnd = checked(headerSize + codeSize);
		int sectionPadding = hasExceptionRegions ? (4 - codeEnd % 4) % 4 : 0;
		bool smallSections = hasExceptionRegions &&
			!options.ForceFatExceptionSections &&
			canUseSmallSections(body, regions, offsets);
		int sectionSize = hasExceptionRegions
			? checked(ehSectionHeaderSize + (smallSections ? smallClauseSize : fatClauseSize) * regions.Count)
			: 0;

		byte[] bytes = new byte[checked(codeEnd + sectionPadding + sectionSize)];
		Span<byte> buffer = bytes;

		if (tinyHeader) {
			buffer[0] = (byte)((codeSize << 2) | tinyFormat);
		} else {
			int flags = fatFormat;
			if (hasExceptionRegions)
				flags |= moreSects;
			if (body.InitLocals && !body.Locals.IsEmpty)
				flags |= initLocalsFlag;
			BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)((3 << 12) | flags));
			BinaryPrimitives.WriteUInt16LittleEndian(buffer[2..], checked((ushort)maxStack));
			BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], codeSize);
			BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], resolveLocalsToken(body, resolver));
		}

		writeCode(body, instrs, forms, offsets, operandTokens, prefixTokens, buffer.Slice(headerSize, codeSize));

		if (hasExceptionRegions)
			writeExceptionSections(body, regions, offsets, resolver, smallSections, buffer[(codeEnd + sectionPadding)..]);

		return new IlEncodedMethodBody(bytes, headerSize, codeSize, maxStack);
	}

	// ==========================================================================================
	// token resolution
	private static void resolveTokens(
		IReadOnlyList<IlInstruction> instrs,
		IIlTokenResolver resolver,
		int[] operandTokens,
		int[] prefixTokens
	) {
		for (int i = 0; i < instrs.Count; i++) {
			IlInstruction instr = instrs[i];
			operandTokens[i] = instr.Operand switch {
				IlStringOperand o => userStringToken(resolver, o.Value),
				IlTypeOperand o => typeToken(resolver, o.Type),
				IlMethodOperand o => methodToken(resolver, o.Method),
				IlFieldOperand o => fieldToken(resolver, o.Field),
				IlCallSiteOperand o => signatureToken(resolver, o.Signature),
				_ => 0,
			};
			if (instr.Prefixes is { ConstrainedType: { } constrained })
				prefixTokens[i] = typeToken(resolver, constrained);
		}
	}

	private static int typeToken(IIlTokenResolver resolver, IlTypeRef type) {
		EntityHandle handle = resolver.ResolveType(type);
		return token(handle, type, HandleKind.TypeDefinition, HandleKind.TypeReference, HandleKind.TypeSpecification);
	}

	private static int methodToken(IIlTokenResolver resolver, IlMethodRef method) {
		EntityHandle handle = resolver.ResolveMethod(method);
		return token(handle, method, HandleKind.MethodDefinition, HandleKind.MemberReference, HandleKind.MethodSpecification);
	}

	private static int fieldToken(IIlTokenResolver resolver, IlFieldRef field) {
		EntityHandle handle = resolver.ResolveField(field);
		return token(handle, field, HandleKind.FieldDefinition, HandleKind.MemberReference);
	}

	private static int signatureToken(IIlTokenResolver resolver, IlMethodSignature signature) {
		StandaloneSignatureHandle handle = resolver.ResolveCallSite(signature);
		if (handle.IsNil)
			throw new IlEncodingException($"the token resolver returned a nil handle for call site '{signature}'");
		return MetadataTokens.GetToken(handle);
	}

	private static int userStringToken(IIlTokenResolver resolver, string value) {
		UserStringHandle handle = resolver.ResolveUserString(value);
		if (handle.IsNil)
			throw new IlEncodingException("the token resolver returned a nil handle for a user string");
		return 0x70000000 | MetadataTokens.GetHeapOffset(handle);
	}

	private static int resolveLocalsToken(IlMethodBody body, IIlTokenResolver resolver) {
		if (body.Locals.IsEmpty)
			return 0;
		StandaloneSignatureHandle handle = resolver.ResolveLocals(body.Locals, body.LocalSignatureOrigin);
		if (handle.IsNil)
			throw new IlEncodingException("the token resolver returned a nil handle for the locals signature");
		return MetadataTokens.GetToken(handle);
	}

	private static int token(EntityHandle handle, object reference, HandleKind first, HandleKind second) {
		if (handle.IsNil)
			throw new IlEncodingException($"the token resolver returned a nil handle for '{reference}'");
		if (handle.Kind != first && handle.Kind != second)
			throw new IlEncodingException($"the token resolver returned a {handle.Kind} handle for '{reference}'");
		return MetadataTokens.GetToken(handle);
	}

	private static int token(EntityHandle handle, object reference, HandleKind first, HandleKind second, HandleKind third) {
		if (handle.IsNil)
			throw new IlEncodingException($"the token resolver returned a nil handle for '{reference}'");
		if (handle.Kind != first && handle.Kind != second && handle.Kind != third)
			throw new IlEncodingException($"the token resolver returned a {handle.Kind} handle for '{reference}'");
		return MetadataTokens.GetToken(handle);
	}

	// ==========================================================================================
	// layout
	private static int prefixSize(IlInstructionPrefixes? prefixes) {
		if (prefixes is null)
			return 0;
		int size = 0;
		if (prefixes.Has(IlPrefixFlags.No))
			size += 3;
		if (prefixes.Has(IlPrefixFlags.ReadOnly))
			size += 2;
		if (prefixes.Has(IlPrefixFlags.Unaligned))
			size += 3;
		if (prefixes.Has(IlPrefixFlags.Volatile))
			size += 2;
		if (prefixes.Has(IlPrefixFlags.Tail))
			size += 2;
		if (prefixes.Has(IlPrefixFlags.Constrained))
			size += 6;
		return size;
	}

	private static EmittedForm selectForm(IlInstruction instr, bool compact) {
		switch (instr.OpCode) {
		case ILOpCode.Ldarg: {
			int index = argumentIndex(instr);
			if (compact && index <= 3)
				return new EmittedForm((ILOpCode)((int)ILOpCode.Ldarg_0 + index), 0);
			return compact && index <= byte.MaxValue
				? new EmittedForm(ILOpCode.Ldarg_s, 1)
				: new EmittedForm(ILOpCode.Ldarg, 2);
		}
		case ILOpCode.Ldarga: {
			int index = argumentIndex(instr);
			return compact && index <= byte.MaxValue
				? new EmittedForm(ILOpCode.Ldarga_s, 1)
				: new EmittedForm(ILOpCode.Ldarga, 2);
		}
		case ILOpCode.Starg: {
			int index = argumentIndex(instr);
			return compact && index <= byte.MaxValue
				? new EmittedForm(ILOpCode.Starg_s, 1)
				: new EmittedForm(ILOpCode.Starg, 2);
		}
		case ILOpCode.Ldloc: {
			int index = localIndex(instr);
			if (compact && index <= 3)
				return new EmittedForm((ILOpCode)((int)ILOpCode.Ldloc_0 + index), 0);
			return compact && index <= byte.MaxValue
				? new EmittedForm(ILOpCode.Ldloc_s, 1)
				: new EmittedForm(ILOpCode.Ldloc, 2);
		}
		case ILOpCode.Ldloca: {
			int index = localIndex(instr);
			return compact && index <= byte.MaxValue
				? new EmittedForm(ILOpCode.Ldloca_s, 1)
				: new EmittedForm(ILOpCode.Ldloca, 2);
		}
		case ILOpCode.Stloc: {
			int index = localIndex(instr);
			if (compact && index <= 3)
				return new EmittedForm((ILOpCode)((int)ILOpCode.Stloc_0 + index), 0);
			return compact && index <= byte.MaxValue
				? new EmittedForm(ILOpCode.Stloc_s, 1)
				: new EmittedForm(ILOpCode.Stloc, 2);
		}
		case ILOpCode.Ldc_i4: {
			int value = int32Value(instr);
			if (compact && value is >= -1 and <= 8)
				return new EmittedForm(
					value == -1 ? ILOpCode.Ldc_i4_m1 : (ILOpCode)((int)ILOpCode.Ldc_i4_0 + value),
					0
				);
			return compact && value is >= sbyte.MinValue and <= sbyte.MaxValue
				? new EmittedForm(ILOpCode.Ldc_i4_s, 1)
				: new EmittedForm(ILOpCode.Ldc_i4, 4);
		}
		case ILOpCode.Switch: {
			if (instr.Operand is not IlSwitchOperand @switch)
				throw new InternalStateException($"instruction {instr.Id} is a switch without a switch operand");
			return new EmittedForm(ILOpCode.Switch, checked(4 + 4 * @switch.Targets.Length));
		}
		default:
			return new EmittedForm(instr.OpCode, IlOpCodeInfo.GetOperandSize(IlOpCodeInfo.GetOperandEncoding(instr.OpCode)));
		}
	}

	private static int argumentIndex(IlInstruction instr) {
		if (instr.Operand is not IlArgumentOperand argument)
			throw new InternalStateException($"instruction {instr.Id} does not have an argument operand");
		if ((uint)argument.Index > ushort.MaxValue)
			throw new IlEncodingException($"argument index {argument.Index} does not fit in an IL argument operand");
		return argument.Index;
	}

	private static int localIndex(IlInstruction instr) {
		if (instr.Operand is not IlLocalOperand local)
			throw new InternalStateException($"instruction {instr.Id} does not have a local operand");
		if ((uint)local.Index > ushort.MaxValue)
			throw new IlEncodingException($"local index {local.Index} does not fit in an IL local operand");
		return local.Index;
	}

	private static int int32Value(IlInstruction instr) =>
		instr.Operand is IlInt32Operand value
			? value.Value
			: throw new InternalStateException($"instruction {instr.Id} does not have an int32 operand");

	// ==========================================================================================
	// code stream
	private static void writeCode(
		IlMethodBody body,
		IReadOnlyList<IlInstruction> instrs,
		EmittedForm[] forms,
		int[] offsets,
		int[] operandTokens,
		int[] prefixTokens,
		Span<byte> code
	) {
		int position = 0;
		for (int i = 0; i < instrs.Count; i++) {
			IlInstruction instr = instrs[i];
			if (instr.Prefixes is {} prefixes)
				writePrefixes(prefixes, prefixTokens[i], code, ref position);

			EmittedForm form = forms[i];
			writeOpCode(form.OpCode, code, ref position);

			IlOperandEncoding encoding = IlOpCodeInfo.GetOperandEncoding(form.OpCode);
			switch (encoding) {
			case IlOperandEncoding.None:
				break;
			case IlOperandEncoding.Int8:
				code[position++] = unchecked((byte)(sbyte)int32Value(instr));
				break;
			case IlOperandEncoding.Int32:
				BinaryPrimitives.WriteInt32LittleEndian(code[position..], int32Value(instr));
				position += 4;
				break;
			case IlOperandEncoding.Int64:
				BinaryPrimitives.WriteInt64LittleEndian(code[position..], ((IlInt64Operand)instr.Operand).Value);
				position += 8;
				break;
			case IlOperandEncoding.Float32:
				BinaryPrimitives.WriteSingleLittleEndian(code[position..], ((IlFloat32Operand)instr.Operand).Value);
				position += 4;
				break;
			case IlOperandEncoding.Float64:
				BinaryPrimitives.WriteDoubleLittleEndian(code[position..], ((IlFloat64Operand)instr.Operand).Value);
				position += 8;
				break;
			case IlOperandEncoding.Argument8:
				code[position++] = (byte)argumentIndex(instr);
				break;
			case IlOperandEncoding.Argument16:
				BinaryPrimitives.WriteUInt16LittleEndian(code[position..], (ushort)argumentIndex(instr));
				position += 2;
				break;
			case IlOperandEncoding.Local8:
				code[position++] = (byte)localIndex(instr);
				break;
			case IlOperandEncoding.Local16:
				BinaryPrimitives.WriteUInt16LittleEndian(code[position..], (ushort)localIndex(instr));
				position += 2;
				break;
			case IlOperandEncoding.Branch32: {
				int target = anchorOffset(body, offsets, branchTarget(instr));
				BinaryPrimitives.WriteInt32LittleEndian(code[position..], target - (position + 4));
				position += 4;
				break;
			}
			case IlOperandEncoding.Switch: {
				ImmutableArray<IlAnchorId> targets = ((IlSwitchOperand)instr.Operand).Targets;
				BinaryPrimitives.WriteInt32LittleEndian(code[position..], targets.Length);
				position += 4;
				int afterInstruction = position + 4 * targets.Length;
				foreach (IlAnchorId anchor in targets) {
					BinaryPrimitives.WriteInt32LittleEndian(code[position..], anchorOffset(body, offsets, anchor) - afterInstruction);
					position += 4;
				}
				break;
			}
			case IlOperandEncoding.StringToken:
			case IlOperandEncoding.TypeToken:
			case IlOperandEncoding.MethodToken:
			case IlOperandEncoding.FieldToken:
			case IlOperandEncoding.SignatureToken:
			case IlOperandEncoding.EntityToken:
				BinaryPrimitives.WriteInt32LittleEndian(code[position..], operandTokens[i]);
				position += 4;
				break;
			default:
				throw new InternalStateException($"instruction {instr.Id} has unencodable operand encoding '{encoding}'");
			}

			if (position != offsets[i + 1])
				throw new InternalStateException($"instruction {instr.Id} encoded to offset {position}, but layout predicted {offsets[i + 1]}");
		}
	}

	private static void writePrefixes(IlInstructionPrefixes prefixes, int constrainedToken, Span<byte> code, ref int position) {
		if (prefixes.Has(IlPrefixFlags.No)) {
			writeOpCode(IlOpCodeInfo.No, code, ref position);
			code[position++] = (byte)prefixes.SkipChecks;
		}
		if (prefixes.Has(IlPrefixFlags.ReadOnly))
			writeOpCode(ILOpCode.Readonly, code, ref position);
		if (prefixes.Has(IlPrefixFlags.Unaligned)) {
			writeOpCode(ILOpCode.Unaligned, code, ref position);
			code[position++] = prefixes.Alignment;
		}
		if (prefixes.Has(IlPrefixFlags.Volatile))
			writeOpCode(ILOpCode.Volatile, code, ref position);
		if (prefixes.Has(IlPrefixFlags.Tail))
			writeOpCode(ILOpCode.Tail, code, ref position);
		if (prefixes.Has(IlPrefixFlags.Constrained)) {
			writeOpCode(ILOpCode.Constrained, code, ref position);
			BinaryPrimitives.WriteInt32LittleEndian(code[position..], constrainedToken);
			position += 4;
		}
	}

	private static void writeOpCode(ILOpCode opCode, Span<byte> code, ref int position) {
		int value = (int)opCode;
		if (value >= 0xfe00) {
			code[position++] = 0xfe;
			code[position++] = (byte)value;
		} else {
			code[position++] = (byte)value;
		}
	}

	private static IlAnchorId branchTarget(IlInstruction instr) =>
		instr.Operand is IlBranchOperand branch
			? branch.Target
			: throw new InternalStateException($"instruction {instr.Id} does not have a branch operand");

	private static int anchorOffset(IlMethodBody body, int[] offsets, IlAnchorId anchor) =>
		body.TryGetAnchorBoundary(anchor, out int boundary)
			? offsets[boundary]
			: throw new InternalStateException($"anchor {anchor} does not belong to this body");

	// ==========================================================================================
	// exception sections
	private static bool canUseSmallSections(IlMethodBody body, IReadOnlyList<IlExceptionRegion> regions, int[] offsets) {
		if (regions.Count > maxSmallClauses)
			return false;
		foreach (IlExceptionRegion region in regions) {
			int tryOffset = anchorOffset(body, offsets, region.TryStart);
			int tryLength = anchorOffset(body, offsets, region.TryEnd) - tryOffset;
			int handlerOffset = anchorOffset(body, offsets, region.HandlerStart);
			int handlerLength = anchorOffset(body, offsets, region.HandlerEnd) - handlerOffset;
			if (
				tryOffset > ushort.MaxValue || handlerOffset > ushort.MaxValue ||
				tryLength > byte.MaxValue || handlerLength > byte.MaxValue
			)
				return false;
		}
		return true;
	}

	private static void writeExceptionSections(
		IlMethodBody body,
		IReadOnlyList<IlExceptionRegion> regions,
		int[] offsets,
		IIlTokenResolver resolver,
		bool small,
		Span<byte> section
	) {
		int dataSize = ehSectionHeaderSize + (small ? smallClauseSize : fatClauseSize) * regions.Count;
		int pos = 0;

		if (small) {
			section[pos++] = ehTable;
			section[pos++] = checked((byte)dataSize);
			section[pos++] = 0;
			section[pos++] = 0;
		} else {
			section[pos++] = ehTable | ehFatFormat;
			section[pos++] = (byte)dataSize;
			section[pos++] = (byte)(dataSize >> 8);
			section[pos++] = (byte)(dataSize >> 16);
		}

		foreach (IlExceptionRegion region in regions) {
			int flags = region.Kind switch {
				IlExceptionRegionKind.Catch => clauseException,
				IlExceptionRegionKind.Filter => clauseFilter,
				IlExceptionRegionKind.Finally => clauseFinally,
				IlExceptionRegionKind.Fault => clauseFault,
				_ => throw new InternalStateException($"unknown exception region kind '{region.Kind}'"),
			};
			int tryOffset = anchorOffset(body, offsets, region.TryStart);
			int tryLength = anchorOffset(body, offsets, region.TryEnd) - tryOffset;
			int handlerOffset = anchorOffset(body, offsets, region.HandlerStart);
			int handlerLength = anchorOffset(body, offsets, region.HandlerEnd) - handlerOffset;
			int classTokenOrFilterOffset = region.Kind switch {
				IlExceptionRegionKind.Catch => typeToken(
					resolver,
					region.CatchType ?? throw new InternalStateException("catch region has no catch type")
				),
				IlExceptionRegionKind.Filter => anchorOffset(
					body,
					offsets,
					region.FilterStart ?? throw new InternalStateException("filter region has no filter start")
				),
				_ => 0,
			};

			if (small) {
				BinaryPrimitives.WriteUInt16LittleEndian(section[pos..], (ushort)flags);
				pos += 2;
				BinaryPrimitives.WriteUInt16LittleEndian(section[pos..], (ushort)tryOffset);
				pos += 2;
				section[pos++] = (byte)tryLength;
				BinaryPrimitives.WriteUInt16LittleEndian(section[pos..], (ushort)handlerOffset);
				pos += 2;
				section[pos++] = (byte)handlerLength;
			} else {
				BinaryPrimitives.WriteInt32LittleEndian(section[pos..], flags);
				pos += 4;
				BinaryPrimitives.WriteInt32LittleEndian(section[pos..], tryOffset);
				pos += 4;
				BinaryPrimitives.WriteInt32LittleEndian(section[pos..], tryLength);
				pos += 4;
				BinaryPrimitives.WriteInt32LittleEndian(section[pos..], handlerOffset);
				pos += 4;
				BinaryPrimitives.WriteInt32LittleEndian(section[pos..], handlerLength);
				pos += 4;
			}
			BinaryPrimitives.WriteInt32LittleEndian(section[pos..], classTokenOrFilterOffset);
			pos += 4;
		}

		if (pos != dataSize)
			throw new InternalStateException($"exception section encoded to {pos} bytes, but layout predicted {dataSize}");
	}
}
