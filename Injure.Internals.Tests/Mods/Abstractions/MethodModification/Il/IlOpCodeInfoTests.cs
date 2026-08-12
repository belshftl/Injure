// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Internals.Tests.Mods.Abstractions.MethodModification.Il;

public sealed class IlOpCodeInfoTests {
	private static readonly Lazy<TheoryData<string>> allOpCodes = new(static () => {
		TheoryData<string> data = new();
		foreach (FieldInfo fld in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
			// Prefix1-7 and Prefixref are reserved multibyte escape codes, not instructions
			if (fld.FieldType == typeof(OpCode) && !fld.Name.StartsWith("Prefix", StringComparison.Ordinal))
				data.Add(fld.Name);
		return data;
	});
	public static TheoryData<string> AllOpCodes => allOpCodes.Value;
	private static OpCode reflect(string name) =>
		(OpCode)typeof(OpCodes).GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

	[Theory]
	[MemberData(nameof(AllOpCodes))]
	public static void EveryRuntimeOpCodeIsDefined(string name) {
		OpCode expected = reflect(name);
		var opCode = (ILOpCode)(ushort)expected.Value;
		IlOpCodeDescriptor desc = IlOpCodeInfo.GetDescriptor(opCode);
		Assert.True(desc.IsDefined, $"{name} (0x{(ushort)expected.Value:x4}) is missing from the table");
		Assert.Equal(expected.Size, IlOpCodeInfo.GetOpCodeSize(opCode));
	}

	[Theory]
	[MemberData(nameof(AllOpCodes))]
	public static void OperandSizeMatchesRuntime(string name) {
		OpCode expected = reflect(name);
		var opCode = (ILOpCode)(ushort)expected.Value;
		IlOperandEncoding encoding = IlOpCodeInfo.GetOperandEncoding(opCode);
		if (expected.OperandType == OperandType.InlineSwitch) {
			Assert.Equal(IlOperandEncoding.Switch, encoding);
			return;
		}
		Assert.Equal(operandSize(expected.OperandType), IlOpCodeInfo.GetOperandSize(encoding));
	}

	[Theory]
	[MemberData(nameof(AllOpCodes))]
	public static void StackEffectMatchesRuntime(string name) {
		OpCode expected = reflect(name);
		var opCode = (ILOpCode)(ushort)expected.Value;
		IlOpCodeDescriptor desc = IlOpCodeInfo.GetDescriptor(opCode);
		int? pop = popCount(expected.StackBehaviourPop);
		int? push = pushCount(expected.StackBehaviourPush);
		if (pop is null || push is null) {
			Assert.True(
				desc.Pop == IlOpCodeInfo.VariableStackEffect || desc.Push == IlOpCodeInfo.VariableStackEffect,
				$"{name} has a variable stack effect at runtime but a fixed one in the table"
			);
			return;
		}
		Assert.Equal(pop.Value, desc.Pop);
		Assert.Equal(push.Value, desc.Push);
	}

	[Theory]
	[MemberData(nameof(AllOpCodes))]
	public static void FlowKindMatchesRuntime(string name) {
		OpCode expected = reflect(name);
		IlFlowKind flow = IlOpCodeInfo.GetFlowKind((ILOpCode)(ushort)expected.Value);
		switch (expected.FlowControl) {
		case FlowControl.Meta:
			Assert.Equal(IlFlowKind.Prefix, flow);
			break;
		case FlowControl.Throw:
			Assert.Equal(IlFlowKind.Throw, flow);
			break;
		case FlowControl.Branch:
			Assert.True(flow is IlFlowKind.Branch or IlFlowKind.Leave, $"{name} is {flow}");
			break;
		case FlowControl.Cond_Branch:
			Assert.True(flow is IlFlowKind.ConditionalBranch or IlFlowKind.Switch, $"{name} is {flow}");
			break;
		case FlowControl.Return:
			Assert.True(flow is IlFlowKind.Return or IlFlowKind.EndFinally or IlFlowKind.EndFilter, $"{name} is {flow}");
			break;
		default:
			Assert.True(flow is IlFlowKind.Next or IlFlowKind.Jmp, $"{name} is {flow}");
			break;
		}
	}

	[Theory]
	[MemberData(nameof(AllOpCodes))]
	public static void CanonicalFormIsItselfDefinedAndIdempotent(string name) {
		var opCode = (ILOpCode)(ushort)reflect(name).Value;
		ILOpCode canonical = IlOpCodeInfo.Canonicalize(opCode);
		Assert.True(IlOpCodeInfo.IsDefined(canonical), $"canonical form of {name} is not defined");
		Assert.Equal(canonical, IlOpCodeInfo.Canonicalize(canonical));
	}

	[Theory]
	[MemberData(nameof(AllOpCodes))]
	public static void AliasSharesStackEffectWithCanonicalForm(string name) {
		var opCode = (ILOpCode)(ushort)reflect(name).Value;
		IlOpCodeDescriptor alias = IlOpCodeInfo.GetDescriptor(opCode);
		IlOpCodeDescriptor canonical = IlOpCodeInfo.GetDescriptor(alias.Canonical);
		Assert.Equal(canonical.Pop, alias.Pop);
		Assert.Equal(canonical.Push, alias.Push);
		Assert.Equal(canonical.Flow, alias.Flow);
	}

	[Fact]
	public void NoPrefixIsDefinedDespiteRuntimeTableOmission() {
		IlOpCodeDescriptor desc = IlOpCodeInfo.GetDescriptor(IlOpCodeInfo.No);
		Assert.True(desc.IsDefined);
		Assert.Equal(IlPrefixKind.No, desc.Prefix);
		Assert.Equal(IlFlowKind.Prefix, desc.Flow);
		Assert.Equal(1, IlOpCodeInfo.GetOperandSize(desc.Encoding));
		Assert.DoesNotContain(
			typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static),
			f => f.FieldType == typeof(OpCode) && (ushort)((OpCode)f.GetValue(null)!).Value == (ushort)IlOpCodeInfo.No
		);
	}

	[Theory]
	[InlineData(0x00fe)] // the two-byte escape is not an opcode on its own
	[InlineData(0x00ff)]
	[InlineData(0xfe1f)] // past the end of the two-byte range
	[InlineData(0xfeff)]
	[InlineData(0xff00)]
	public static void UndefinedOpCodesAreRejected(int raw) =>
		Assert.False(IlOpCodeInfo.IsDefined((ILOpCode)raw));

	[Theory]
	[InlineData(0xf8)]
	[InlineData(0xf9)]
	[InlineData(0xfa)]
	[InlineData(0xfb)]
	[InlineData(0xfc)]
	[InlineData(0xfd)]
	[InlineData(0xfe)]
	[InlineData(0xff)]
	public static void ReservedEscapeCodesAreNotInstructions(int raw) =>
		Assert.False(IlOpCodeInfo.IsDefined((ILOpCode)raw));

	[Fact]
	public void EveryPrefixIsClassifiedAsOne() {
		ILOpCode[] prefixes = [
			ILOpCode.Constrained, ILOpCode.Volatile, ILOpCode.Tail,
			ILOpCode.Unaligned, ILOpCode.Readonly, IlOpCodeInfo.No,
		];
		foreach (ILOpCode prefix in prefixes) {
			IlOpCodeDescriptor descriptor = IlOpCodeInfo.GetDescriptor(prefix);
			Assert.True(IlOpCodeInfo.IsPrefix(prefix), $"{prefix} is not classified as a prefix");
			Assert.Equal(IlFlowKind.Prefix, descriptor.Flow);
			Assert.Equal(0, descriptor.Pop);
			Assert.Equal(0, descriptor.Push);
		}
	}

	[Fact]
	public void BranchClassificationCoversBothOperandWidths() {
		Assert.True(IlOpCodeInfo.IsBranch(ILOpCode.Br));
		Assert.True(IlOpCodeInfo.IsBranch(ILOpCode.Br_s));
		Assert.True(IlOpCodeInfo.IsBranch(ILOpCode.Leave_s));

		// switch branches but carries a target list rather than a single offset
		Assert.False(IlOpCodeInfo.IsBranch(ILOpCode.Switch));
		Assert.False(IlOpCodeInfo.IsBranch(ILOpCode.Ret));
	}

	private static int operandSize(OperandType operandType) => operandType switch {
	    OperandType.InlineNone => 0,
	    OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
	    OperandType.InlineVar => 2,
	    OperandType.InlineI8 or OperandType.InlineR => 8,
		_ => 4,
	};

	private static int? popCount(StackBehaviour behaviour) => behaviour switch {
		StackBehaviour.Pop0 => 0,
		StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
		StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or
			StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or
			StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
		StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or
			StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or
			StackBehaviour.Popref_popi_popref or StackBehaviour.Popref_popi_pop1 => 3,
		_ => null,
	};

	private static int? pushCount(StackBehaviour behaviour) => behaviour switch {
		StackBehaviour.Push0 => 0,
		StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or
			StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
		StackBehaviour.Push1_push1 => 2,
		_ => null,
	};
}
