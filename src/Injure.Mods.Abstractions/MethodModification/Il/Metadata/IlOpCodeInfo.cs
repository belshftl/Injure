// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.MethodModification.Il.Metadata;

internal enum IlOperandEncoding : byte {
	None,
	Int8,
	UInt8,
	Int32,
	Int64,
	Float32,
	Float64,
	Argument8,
	Argument16,
	Local8,
	Local16,
	Branch8,
	Branch32,
	Switch,
	StringToken,
	TypeToken,
	MethodToken,
	FieldToken,
	SignatureToken,
	EntityToken,
}

internal enum IlFlowKind : byte {
	/// <summary>
	/// Falls through to the next instruction and has no other successor.
	/// </summary>
	Next,

	/// <summary>
	/// Transfers control to its branch target and does not fall through.
	/// </summary>
	Branch,

	/// <summary>
	/// Transfers control to its branch target or falls through.
	/// </summary>
	ConditionalBranch,

	/// <summary>
	/// Transfers control to any switch target or falls through.
	/// </summary>
	Switch,

	/// <summary>
	/// Returns from the method.
	/// </summary>
	Return,

	/// <summary>
	/// Raises an exception; no successor.
	/// </summary>
	Throw,

	/// <summary>
	/// Exits a protected region, emptying the stack.
	/// </summary>
	Leave,

	/// <summary>
	/// Ends a finally/fault handler; no successor.
	/// </summary>
	EndFinally,

	/// <summary>
	/// Ends a filter expression; no successor.
	/// </summary>
	EndFilter,

	/// <summary>
	/// Transfers control to another method entirely; no successor.
	/// </summary>
	Jmp,

	/// <summary>
	/// An instruction prefix, bundled onto the following instruction.
	/// </summary>
	Prefix,
}

internal enum IlPrefixKind : byte {
	None,
	Constrained,
	Volatile,
	Tail,
	Unaligned,
	ReadOnly,
	No,
}

internal readonly struct IlOpCodeDescriptor(
	ILOpCode canonical,
	IlOperandEncoding encoding,
	IlFlowKind flow,
	IlPrefixKind prefix,
	int pop,
	int push
) {
	public ILOpCode Canonical { get; } = canonical;
	public IlOperandEncoding Encoding { get; } = encoding;
	public IlFlowKind Flow { get; } = flow;
	public IlPrefixKind Prefix { get; } = prefix;
	public sbyte Pop { get; } = (sbyte)pop;
	public sbyte Push { get; } = (sbyte)push;
	public bool IsDefined { get; } = true;
}

internal static class IlOpCodeInfo {
	/// <summary>
	/// Sentinel <see cref="IlOpCodeDescriptor.Pop"/>/<see cref="IlOpCodeDescriptor.Push"/> value for
	/// instructions whose stack effect depends on a signature.
	/// </summary>
	public const sbyte VariableStackEffect = -1;

	/// <summary>
	/// The <c>no.</c> prefix, which is absent from <see cref="ILOpCode"/>.
	/// </summary>
	public const ILOpCode No = (ILOpCode)0xfe19;

	private const int singleByteCount = 0x100;
	private const int extendedCount = 0x20;
	private const int tableSize = singleByteCount + extendedCount;

	private static readonly IlOpCodeDescriptor[] descriptors = build();

	public static IlOpCodeDescriptor GetDescriptor(ILOpCode opCode) {
		int i = index(opCode);
		return (uint)i < (uint)descriptors.Length ? descriptors[i] : default;
	}

	public static bool IsDefined(ILOpCode opCode) => GetDescriptor(opCode).IsDefined;

	public static ILOpCode Canonicalize(ILOpCode opCode) {
		IlOpCodeDescriptor descriptor = GetDescriptor(opCode);
		return descriptor.IsDefined ? descriptor.Canonical : opCode;
	}

	public static IlOperandEncoding GetOperandEncoding(ILOpCode opCode) => GetDescriptor(opCode).Encoding;
	public static IlFlowKind GetFlowKind(ILOpCode opCode) => GetDescriptor(opCode).Flow;
	public static bool IsPrefix(ILOpCode opCode) => GetDescriptor(opCode).Prefix != IlPrefixKind.None;
	public static bool IsBranch(ILOpCode opCode) =>
		GetDescriptor(opCode).Encoding is IlOperandEncoding.Branch8 or IlOperandEncoding.Branch32;
	public static int GetOpCodeSize(ILOpCode opCode) => (int)opCode >= 0xfe00 ? 2 : 1;
	public static int GetOperandSize(IlOperandEncoding encoding) => encoding switch {
		IlOperandEncoding.None => 0,
		IlOperandEncoding.Int8 or IlOperandEncoding.UInt8 or IlOperandEncoding.Argument8 or
			IlOperandEncoding.Local8 or IlOperandEncoding.Branch8 => 1,
		IlOperandEncoding.Argument16 or IlOperandEncoding.Local16 => 2,
		IlOperandEncoding.Int32 or IlOperandEncoding.Float32 or IlOperandEncoding.Branch32 or
			IlOperandEncoding.StringToken or IlOperandEncoding.TypeToken or IlOperandEncoding.MethodToken or
			IlOperandEncoding.FieldToken or IlOperandEncoding.SignatureToken or IlOperandEncoding.EntityToken => 4,
		IlOperandEncoding.Int64 or IlOperandEncoding.Float64 => 8,
		_ => throw new InternalStateException($"operand encoding '{encoding}' has no fixed size"),
	};

	private static int index(ILOpCode opCode) {
		int value = (int)opCode;
		if (value < 0xfe)
			return value;
		if ((value & 0xff00) == 0xfe00 && (value & 0xff) < extendedCount)
			return singleByteCount + (value & 0xff);
		return -1;
	}

	private static IlOpCodeDescriptor[] build() {
		var table = new IlOpCodeDescriptor[tableSize];
		void def(
			ILOpCode code,
			IlOperandEncoding encoding,
			int pop,
			int push,
			IlFlowKind flow = IlFlowKind.Next,
			IlPrefixKind prefix = IlPrefixKind.None
		) {
			int i = index(code);
			if (i < 0)
				throw new InternalStateException($"opcode 0x{(int)code:x4} is outside the opcode table");
			if (table[i].IsDefined)
				throw new InternalStateException($"opcode 0x{(int)code:x4} is defined twice");
			table[i] = new IlOpCodeDescriptor(code, encoding, flow, prefix, pop, push);
		}

		void alias(ILOpCode code, ILOpCode canonical, IlOperandEncoding encoding) {
			int i = index(code);
			int target = index(canonical);
			if (i < 0 || target < 0)
				throw new InternalStateException($"opcode 0x{(int)code:x4} is outside the opcode table");
			if (!table[target].IsDefined)
				throw new InternalStateException($"canonical opcode 0x{(int)canonical:x4} is not defined yet");
			if (table[i].IsDefined)
				throw new InternalStateException($"opcode 0x{(int)code:x4} is defined twice");
			IlOpCodeDescriptor c = table[target];
			table[i] = new IlOpCodeDescriptor(canonical, encoding, c.Flow, IlPrefixKind.None, c.Pop, c.Push);
		}

		void nullary(ILOpCode code, int pop, int push) => def(code, IlOperandEncoding.None, pop, push);

		// ----------------------------------------------------------------------------------
		// misc, constants, stack
		nullary(ILOpCode.Nop, 0, 0);
		nullary(ILOpCode.Break, 0, 0);
		nullary(ILOpCode.Ldnull, 0, 1);
		nullary(ILOpCode.Dup, 1, 2);
		nullary(ILOpCode.Pop, 1, 0);
		nullary(ILOpCode.Arglist, 0, 1);
		nullary(ILOpCode.Localloc, 1, 1);
		nullary(ILOpCode.Ckfinite, 1, 1);

		def(ILOpCode.Ldc_i4, IlOperandEncoding.Int32, 0, 1);
		alias(ILOpCode.Ldc_i4_m1, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_0, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_1, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_2, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_3, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_4, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_5, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_6, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_7, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_8, ILOpCode.Ldc_i4, IlOperandEncoding.None);
		alias(ILOpCode.Ldc_i4_s, ILOpCode.Ldc_i4, IlOperandEncoding.Int8);
		def(ILOpCode.Ldc_i8, IlOperandEncoding.Int64, 0, 1);
		def(ILOpCode.Ldc_r4, IlOperandEncoding.Float32, 0, 1);
		def(ILOpCode.Ldc_r8, IlOperandEncoding.Float64, 0, 1);
		def(ILOpCode.Ldstr, IlOperandEncoding.StringToken, 0, 1);

		// ----------------------------------------------------------------------------------
		// arguments and locals
		def(ILOpCode.Ldarg, IlOperandEncoding.Argument16, 0, 1);
		alias(ILOpCode.Ldarg_0, ILOpCode.Ldarg, IlOperandEncoding.None);
		alias(ILOpCode.Ldarg_1, ILOpCode.Ldarg, IlOperandEncoding.None);
		alias(ILOpCode.Ldarg_2, ILOpCode.Ldarg, IlOperandEncoding.None);
		alias(ILOpCode.Ldarg_3, ILOpCode.Ldarg, IlOperandEncoding.None);
		alias(ILOpCode.Ldarg_s, ILOpCode.Ldarg, IlOperandEncoding.Argument8);
		def(ILOpCode.Ldarga, IlOperandEncoding.Argument16, 0, 1);
		alias(ILOpCode.Ldarga_s, ILOpCode.Ldarga, IlOperandEncoding.Argument8);
		def(ILOpCode.Starg, IlOperandEncoding.Argument16, 1, 0);
		alias(ILOpCode.Starg_s, ILOpCode.Starg, IlOperandEncoding.Argument8);

		def(ILOpCode.Ldloc, IlOperandEncoding.Local16, 0, 1);
		alias(ILOpCode.Ldloc_0, ILOpCode.Ldloc, IlOperandEncoding.None);
		alias(ILOpCode.Ldloc_1, ILOpCode.Ldloc, IlOperandEncoding.None);
		alias(ILOpCode.Ldloc_2, ILOpCode.Ldloc, IlOperandEncoding.None);
		alias(ILOpCode.Ldloc_3, ILOpCode.Ldloc, IlOperandEncoding.None);
		alias(ILOpCode.Ldloc_s, ILOpCode.Ldloc, IlOperandEncoding.Local8);
		def(ILOpCode.Ldloca, IlOperandEncoding.Local16, 0, 1);
		alias(ILOpCode.Ldloca_s, ILOpCode.Ldloca, IlOperandEncoding.Local8);
		def(ILOpCode.Stloc, IlOperandEncoding.Local16, 1, 0);
		alias(ILOpCode.Stloc_0, ILOpCode.Stloc, IlOperandEncoding.None);
		alias(ILOpCode.Stloc_1, ILOpCode.Stloc, IlOperandEncoding.None);
		alias(ILOpCode.Stloc_2, ILOpCode.Stloc, IlOperandEncoding.None);
		alias(ILOpCode.Stloc_3, ILOpCode.Stloc, IlOperandEncoding.None);
		alias(ILOpCode.Stloc_s, ILOpCode.Stloc, IlOperandEncoding.Local8);

		// ----------------------------------------------------------------------------------
		// calls and returns
		def(ILOpCode.Jmp, IlOperandEncoding.MethodToken, 0, 0, IlFlowKind.Jmp);
		def(ILOpCode.Call, IlOperandEncoding.MethodToken, VariableStackEffect, VariableStackEffect);
		def(ILOpCode.Callvirt, IlOperandEncoding.MethodToken, VariableStackEffect, VariableStackEffect);
		def(ILOpCode.Calli, IlOperandEncoding.SignatureToken, VariableStackEffect, VariableStackEffect);
		def(ILOpCode.Newobj, IlOperandEncoding.MethodToken, VariableStackEffect, VariableStackEffect);
		def(ILOpCode.Ret, IlOperandEncoding.None, VariableStackEffect, 0, IlFlowKind.Return);

		// ----------------------------------------------------------------------------------
		// branches
		def(ILOpCode.Br, IlOperandEncoding.Branch32, 0, 0, IlFlowKind.Branch);
		alias(ILOpCode.Br_s, ILOpCode.Br, IlOperandEncoding.Branch8);
		def(ILOpCode.Leave, IlOperandEncoding.Branch32, 0, 0, IlFlowKind.Leave);
		alias(ILOpCode.Leave_s, ILOpCode.Leave, IlOperandEncoding.Branch8);
		def(ILOpCode.Switch, IlOperandEncoding.Switch, 1, 0, IlFlowKind.Switch);

		def(ILOpCode.Brfalse, IlOperandEncoding.Branch32, 1, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Brfalse_s, ILOpCode.Brfalse, IlOperandEncoding.Branch8);
		def(ILOpCode.Brtrue, IlOperandEncoding.Branch32, 1, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Brtrue_s, ILOpCode.Brtrue, IlOperandEncoding.Branch8);

		def(ILOpCode.Beq, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Beq_s, ILOpCode.Beq, IlOperandEncoding.Branch8);
		def(ILOpCode.Bge, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Bge_s, ILOpCode.Bge, IlOperandEncoding.Branch8);
		def(ILOpCode.Bgt, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Bgt_s, ILOpCode.Bgt, IlOperandEncoding.Branch8);
		def(ILOpCode.Ble, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Ble_s, ILOpCode.Ble, IlOperandEncoding.Branch8);
		def(ILOpCode.Blt, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Blt_s, ILOpCode.Blt, IlOperandEncoding.Branch8);
		def(ILOpCode.Bne_un, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Bne_un_s, ILOpCode.Bne_un, IlOperandEncoding.Branch8);
		def(ILOpCode.Bge_un, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Bge_un_s, ILOpCode.Bge_un, IlOperandEncoding.Branch8);
		def(ILOpCode.Bgt_un, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Bgt_un_s, ILOpCode.Bgt_un, IlOperandEncoding.Branch8);
		def(ILOpCode.Ble_un, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Ble_un_s, ILOpCode.Ble_un, IlOperandEncoding.Branch8);
		def(ILOpCode.Blt_un, IlOperandEncoding.Branch32, 2, 0, IlFlowKind.ConditionalBranch);
		alias(ILOpCode.Blt_un_s, ILOpCode.Blt_un, IlOperandEncoding.Branch8);

		// ----------------------------------------------------------------------------------
		// exceptions
		def(ILOpCode.Throw, IlOperandEncoding.None, 1, 0, IlFlowKind.Throw);
		def(ILOpCode.Rethrow, IlOperandEncoding.None, 0, 0, IlFlowKind.Throw);
		def(ILOpCode.Endfinally, IlOperandEncoding.None, 0, 0, IlFlowKind.EndFinally);
		def(ILOpCode.Endfilter, IlOperandEncoding.None, 1, 0, IlFlowKind.EndFilter);

		// ----------------------------------------------------------------------------------
		// indirect loads/stores
		nullary(ILOpCode.Ldind_i1, 1, 1);
		nullary(ILOpCode.Ldind_u1, 1, 1);
		nullary(ILOpCode.Ldind_i2, 1, 1);
		nullary(ILOpCode.Ldind_u2, 1, 1);
		nullary(ILOpCode.Ldind_i4, 1, 1);
		nullary(ILOpCode.Ldind_u4, 1, 1);
		nullary(ILOpCode.Ldind_i8, 1, 1);
		nullary(ILOpCode.Ldind_i, 1, 1);
		nullary(ILOpCode.Ldind_r4, 1, 1);
		nullary(ILOpCode.Ldind_r8, 1, 1);
		nullary(ILOpCode.Ldind_ref, 1, 1);
		nullary(ILOpCode.Stind_ref, 2, 0);
		nullary(ILOpCode.Stind_i1, 2, 0);
		nullary(ILOpCode.Stind_i2, 2, 0);
		nullary(ILOpCode.Stind_i4, 2, 0);
		nullary(ILOpCode.Stind_i8, 2, 0);
		nullary(ILOpCode.Stind_r4, 2, 0);
		nullary(ILOpCode.Stind_r8, 2, 0);
		nullary(ILOpCode.Stind_i, 2, 0);

		// ----------------------------------------------------------------------------------
		// arithmetic, bitwise, comparison, conversion
		nullary(ILOpCode.Add, 2, 1);
		nullary(ILOpCode.Sub, 2, 1);
		nullary(ILOpCode.Mul, 2, 1);
		nullary(ILOpCode.Div, 2, 1);
		nullary(ILOpCode.Div_un, 2, 1);
		nullary(ILOpCode.Rem, 2, 1);
		nullary(ILOpCode.Rem_un, 2, 1);
		nullary(ILOpCode.And, 2, 1);
		nullary(ILOpCode.Or, 2, 1);
		nullary(ILOpCode.Xor, 2, 1);
		nullary(ILOpCode.Shl, 2, 1);
		nullary(ILOpCode.Shr, 2, 1);
		nullary(ILOpCode.Shr_un, 2, 1);
		nullary(ILOpCode.Add_ovf, 2, 1);
		nullary(ILOpCode.Add_ovf_un, 2, 1);
		nullary(ILOpCode.Mul_ovf, 2, 1);
		nullary(ILOpCode.Mul_ovf_un, 2, 1);
		nullary(ILOpCode.Sub_ovf, 2, 1);
		nullary(ILOpCode.Sub_ovf_un, 2, 1);
		nullary(ILOpCode.Neg, 1, 1);
		nullary(ILOpCode.Not, 1, 1);
		nullary(ILOpCode.Ceq, 2, 1);
		nullary(ILOpCode.Cgt, 2, 1);
		nullary(ILOpCode.Cgt_un, 2, 1);
		nullary(ILOpCode.Clt, 2, 1);
		nullary(ILOpCode.Clt_un, 2, 1);

		nullary(ILOpCode.Conv_i1, 1, 1);
		nullary(ILOpCode.Conv_i2, 1, 1);
		nullary(ILOpCode.Conv_i4, 1, 1);
		nullary(ILOpCode.Conv_i8, 1, 1);
		nullary(ILOpCode.Conv_r4, 1, 1);
		nullary(ILOpCode.Conv_r8, 1, 1);
		nullary(ILOpCode.Conv_u4, 1, 1);
		nullary(ILOpCode.Conv_u8, 1, 1);
		nullary(ILOpCode.Conv_r_un, 1, 1);
		nullary(ILOpCode.Conv_i, 1, 1);
		nullary(ILOpCode.Conv_u, 1, 1);
		nullary(ILOpCode.Conv_u1, 1, 1);
		nullary(ILOpCode.Conv_u2, 1, 1);
		nullary(ILOpCode.Conv_ovf_i1, 1, 1);
		nullary(ILOpCode.Conv_ovf_i2, 1, 1);
		nullary(ILOpCode.Conv_ovf_i4, 1, 1);
		nullary(ILOpCode.Conv_ovf_i8, 1, 1);
		nullary(ILOpCode.Conv_ovf_u1, 1, 1);
		nullary(ILOpCode.Conv_ovf_u2, 1, 1);
		nullary(ILOpCode.Conv_ovf_u4, 1, 1);
		nullary(ILOpCode.Conv_ovf_u8, 1, 1);
		nullary(ILOpCode.Conv_ovf_i, 1, 1);
		nullary(ILOpCode.Conv_ovf_u, 1, 1);
		nullary(ILOpCode.Conv_ovf_i1_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_i2_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_i4_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_i8_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_u1_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_u2_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_u4_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_u8_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_i_un, 1, 1);
		nullary(ILOpCode.Conv_ovf_u_un, 1, 1);

		// ----------------------------------------------------------------------------------
		// object model
		def(ILOpCode.Cpobj, IlOperandEncoding.TypeToken, 2, 0);
		def(ILOpCode.Ldobj, IlOperandEncoding.TypeToken, 1, 1);
		def(ILOpCode.Stobj, IlOperandEncoding.TypeToken, 2, 0);
		def(ILOpCode.Castclass, IlOperandEncoding.TypeToken, 1, 1);
		def(ILOpCode.Isinst, IlOperandEncoding.TypeToken, 1, 1);
		def(ILOpCode.Box, IlOperandEncoding.TypeToken, 1, 1);
		def(ILOpCode.Unbox, IlOperandEncoding.TypeToken, 1, 1);
		def(ILOpCode.Unbox_any, IlOperandEncoding.TypeToken, 1, 1);
		def(ILOpCode.Newarr, IlOperandEncoding.TypeToken, 1, 1);
		def(ILOpCode.Initobj, IlOperandEncoding.TypeToken, 1, 0);
		def(ILOpCode.Sizeof, IlOperandEncoding.TypeToken, 0, 1);
		def(ILOpCode.Refanyval, IlOperandEncoding.TypeToken, 1, 1);
		def(ILOpCode.Mkrefany, IlOperandEncoding.TypeToken, 1, 1);
		nullary(ILOpCode.Refanytype, 1, 1);
		nullary(ILOpCode.Ldlen, 1, 1);

		def(ILOpCode.Ldfld, IlOperandEncoding.FieldToken, 1, 1);
		def(ILOpCode.Ldflda, IlOperandEncoding.FieldToken, 1, 1);
		def(ILOpCode.Stfld, IlOperandEncoding.FieldToken, 2, 0);
		def(ILOpCode.Ldsfld, IlOperandEncoding.FieldToken, 0, 1);
		def(ILOpCode.Ldsflda, IlOperandEncoding.FieldToken, 0, 1);
		def(ILOpCode.Stsfld, IlOperandEncoding.FieldToken, 1, 0);

		def(ILOpCode.Ldftn, IlOperandEncoding.MethodToken, 0, 1);
		def(ILOpCode.Ldvirtftn, IlOperandEncoding.MethodToken, 1, 1);
		def(ILOpCode.Ldtoken, IlOperandEncoding.EntityToken, 0, 1);

		def(ILOpCode.Ldelema, IlOperandEncoding.TypeToken, 2, 1);
		def(ILOpCode.Ldelem, IlOperandEncoding.TypeToken, 2, 1);
		def(ILOpCode.Stelem, IlOperandEncoding.TypeToken, 3, 0);
		nullary(ILOpCode.Ldelem_i1, 2, 1);
		nullary(ILOpCode.Ldelem_u1, 2, 1);
		nullary(ILOpCode.Ldelem_i2, 2, 1);
		nullary(ILOpCode.Ldelem_u2, 2, 1);
		nullary(ILOpCode.Ldelem_i4, 2, 1);
		nullary(ILOpCode.Ldelem_u4, 2, 1);
		nullary(ILOpCode.Ldelem_i8, 2, 1);
		nullary(ILOpCode.Ldelem_i, 2, 1);
		nullary(ILOpCode.Ldelem_r4, 2, 1);
		nullary(ILOpCode.Ldelem_r8, 2, 1);
		nullary(ILOpCode.Ldelem_ref, 2, 1);
		nullary(ILOpCode.Stelem_i, 3, 0);
		nullary(ILOpCode.Stelem_i1, 3, 0);
		nullary(ILOpCode.Stelem_i2, 3, 0);
		nullary(ILOpCode.Stelem_i4, 3, 0);
		nullary(ILOpCode.Stelem_i8, 3, 0);
		nullary(ILOpCode.Stelem_r4, 3, 0);
		nullary(ILOpCode.Stelem_r8, 3, 0);
		nullary(ILOpCode.Stelem_ref, 3, 0);

		nullary(ILOpCode.Cpblk, 3, 0);
		nullary(ILOpCode.Initblk, 3, 0);

		// ----------------------------------------------------------------------------------
		// prefixes
		def(ILOpCode.Constrained, IlOperandEncoding.TypeToken, 0, 0, IlFlowKind.Prefix, IlPrefixKind.Constrained);
		def(ILOpCode.Volatile, IlOperandEncoding.None, 0, 0, IlFlowKind.Prefix, IlPrefixKind.Volatile);
		def(ILOpCode.Tail, IlOperandEncoding.None, 0, 0, IlFlowKind.Prefix, IlPrefixKind.Tail);
		def(ILOpCode.Unaligned, IlOperandEncoding.UInt8, 0, 0, IlFlowKind.Prefix, IlPrefixKind.Unaligned);
		def(ILOpCode.Readonly, IlOperandEncoding.None, 0, 0, IlFlowKind.Prefix, IlPrefixKind.ReadOnly);
		def(No, IlOperandEncoding.UInt8, 0, 0, IlFlowKind.Prefix, IlPrefixKind.No);

		return table;
	}
}
