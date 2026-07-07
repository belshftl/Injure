// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

/// <summary>
/// Builder for one emitted IL fragment.
/// </summary>
/// <remarks>
/// This type records semantic operations. Labels and managed delegates are resolved only when the
/// overarching manipulation transaction commits.
/// </remarks>
public readonly ref struct IlEmitter {
	private readonly IlFragmentBuilder builder;
	internal IlEmitter(IlFragmentBuilder builder) {
		this.builder = builder ?? throw new InternalStateException("IlEmitted constructed with null builder");
	}

	/// <summary>
	/// Marks the current fragment position as the target of a previously defined label.
	/// </summary>
	public void MarkLabel(IlLabel label) => builder.MarkLabel(label);

	public void Dup() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Dup));
	public void Nop() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Nop));
	public void Pop() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Pop));
	public void Ret() => builder.Emit(IlInstructionSpec.Raw(OpCodes.Ret));
	public void Ldarg(int index) => builder.Emit(IlInstructionSpec.Ldarg(index));
	public void LdcI4(int value) => builder.Emit(IlInstructionSpec.LdcI4(value));
	public void Ldloc(int index) => builder.Emit(IlInstructionSpec.Ldloc(index));
	public void Stloc(int index) => builder.Emit(IlInstructionSpec.Stloc(index));

	public void Call(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Call, method));
	}

	public void Callvirt(MethodReference method) {
		ArgumentNullException.ThrowIfNull(method);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Callvirt, method));
	}

	public void Ldfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldfld, field));
	}

	public void Ldsfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldsfld, field));
	}

	public void Stfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Stfld, field));
	}

	public void Stsfld(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Stsfld, field));
	}

	public void Ldflda(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldflda, field));
	}

	public void Ldsflda(FieldReference field) {
		ArgumentNullException.ThrowIfNull(field);
		builder.Emit(IlInstructionSpec.Raw(OpCodes.Ldsflda, field));
	}

	/// <summary>
	/// Emits a call to a managed delegate.
	/// </summary>
	/// <typeparam name="TDelegate">Delegate type to invoke.</typeparam>
	/// <param name="callback">
	/// Delegate instance retained for as long as the transformed runtime hook remains installed.
	/// </param>
	/// <remarks>
	/// <para>
	/// It is highly recommended that <paramref name="callback"/> is a plain static method;
	/// instance methods or capturing lambdas retain state for what's typically the rest of the
	/// generation, and static lambdas commonly emit worse IL at the callsite.
	/// </para>
	/// <para>
	/// Arguments for the delegate invocation must already be on the evaluation stack. The
	/// delegate's return value, if any, remains on the stack. The exact emitted IL/Cecil instructions
	/// are backend-dependent.
	/// </para>
	/// </remarks>
	public void Delegate<TDelegate>(TDelegate callback) where TDelegate : Delegate {
		ArgumentNullException.ThrowIfNull(callback);
		builder.Emit(IlInstructionSpec.ManagedDelegate(callback));
	}

	public void Br(IlLabel target) => builder.Emit(IlInstructionSpec.Branch(OpCodes.Br, target));
	public void Brtrue(IlLabel target) => builder.Emit(IlInstructionSpec.Branch(OpCodes.Brtrue, target));
	public void Brfalse(IlLabel target) => builder.Emit(IlInstructionSpec.Branch(OpCodes.Brfalse, target));
	public void Leave(IlLabel target) => builder.Emit(IlInstructionSpec.Branch(OpCodes.Leave, target));
	public void Switch(ReadOnlySpan<IlLabel> targets) => builder.Emit(IlInstructionSpec.Switch(targets));

	public void Raw(OpCode opCode) => builder.Emit(IlInstructionSpec.Raw(opCode));
	public void Raw(OpCode opCode, object operand) {
		ArgumentNullException.ThrowIfNull(operand);
		builder.Emit(IlInstructionSpec.Raw(opCode, operand));
	}
}
