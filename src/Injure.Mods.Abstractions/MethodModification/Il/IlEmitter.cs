// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Builder for one emitted IL fragment.
/// </summary>
/// <remarks>
/// <para>
/// Emitted instructions are recorded semantically, not encoded. Compact encoding forms are not
/// preserved; an instruction is stored in its canonical form, so a value that could be written as a
/// short form is indistinguishable afterwards from one that could not, both to later manipulators
/// and to the encoder, which independently chooses the shortest legal encoding.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly ref struct IlEmitter {
	private IlFragmentBuilder builder => field ?? throw new InvalidOperationException("this IlEmitter value is uninitialized/invalid");
	private IlOwnerContext ownerContext {
		get {
			if (!field.IsValid)
				throw new InvalidOperationException("this IlEmitter value is uninitialized/invalid");
			return field;
		}
	}
	private readonly IIlCallDispatch? callDispatch;

	internal IlEmitter(IlFragmentBuilder builder, IlOwnerContext ownerContext, IIlCallDispatch? callDispatch) {
		InternalStateException.ThrowIfNull(builder);
		if (!ownerContext.IsValid)
			throw new InternalStateException("IlEmitter constructed with uninitialized/invalid IlOwnerContext");
		this.builder = builder;
		this.ownerContext = ownerContext;
		this.callDispatch = callDispatch;
	}

	// ======================================================================================
	// non-emission methods

	/// <summary>
	/// Marks the current fragment position as the target of a previously defined label.
	/// </summary>
	public void MarkLabel(IlLabel label) => builder.MarkLabel(label);

	// ======================================================================================
	// non-instruction emission methods

	/// <summary>
	/// Emits a sequence of instructions to call <paramref name="method"/> through a dispatch
	/// table rather than by direct reference. This is the only way to call into a reloadable mod.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="method"/> has no callable entry point (open generic, abstract,
	/// extern with RVA 0, etc.)
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="method"/>'s <b>signature</b> would create an illegal reference to a
	/// reloadable mod; see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// This is different from if <paramref name="method"/> itself would create an illegal reference;
	/// see the paragraph on signature operands' types' restrictions in the aforementioned type docs.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Takes in a reflection method rather than a structural metadata reference because the
	/// target has to already be callable now, which is exactly what reflection is for.
	/// </para>
	/// <para>
	/// The sequence emitted is:
	/// <code>
	/// ldc.i4 &lt;...&gt;
	/// call &lt;...&gt;
	/// calli &lt;signature&gt;
	/// </code>
	/// The emitted opcodes are part of the API and a change to them will be treated like an
	/// API break, but the operands of the <c>ldc.i4</c> and <c>call</c> are not, and should be
	/// treated as opaque magic values that are stable within a process but may change between
	/// multiple runs of the process. <c>&lt;signature&gt;</c> is the signature of
	/// <paramref name="method"/>.
	/// </para>
	/// <para>
	/// Open generics are currently unsupported; <paramref name="method"/> must not have open type
	/// parameters. Support may come later, and if it is added in the future, the emitted instruction
	/// sequence will be different for open generic methods (by necessity).
	/// </para>
	/// <para>
	/// Usable in a non-reloadable mod too, though there it's just unnecessary indirection. Never silently
	/// emits a direct call instead.
	/// </para>
	/// </remarks>
	public void IndirectCall(MethodInfo method) {
		if (callDispatch is null)
			throw new InternalStateException("no indirect call dispatch mechanism available here; if this is in an engine test, pass a nonnull IIlCallDispatch to your IlTransactionCore construction");

		ArgumentNullException.ThrowIfNull(method);
		if (method.IsAbstract)
			throw new ArgumentException("method is abstract and has no callable entry point", nameof(method));
		if (method.IsGenericMethodDefinition || method.DeclaringType?.IsGenericTypeDefinition == true)
			throw new ArgumentException("method is an open generic / on an open generic type and has no callable entry point", nameof(method));
		if (method.IsGenericMethodDefinition || method.DeclaringType?.IsGenericTypeDefinition == true)
			throw new ArgumentException("method is an open generic / on an open generic type and has no callable entry point", nameof(method));
		if (
			method.GetMethodBody() is null &&
			(method.Attributes & MethodAttributes.PinvokeImpl) == 0 &&
			(method.GetMethodImplementationFlags() & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) == 0 &&
			!method.IsDefined(typeof(System.Runtime.CompilerServices.UnsafeAccessorAttribute), inherit: false)
		)
			throw new ArgumentException("method is a method with no CIL body, P/Invoke impl, runtime management flags, or [UnsafeAccessor], i.e. likely extern with RVA 0", nameof(method));

		IlMethodSignature signature = IlReferenceFactory.Method(method).Signature;
		IlTypeRestrictionCheck.AssertUnrestricted($"calli {signature}", IlTypeRestrictionCheck.CheckSignature(signature, ownerContext));
		int slot = callDispatch.AllocateSlot(method);
		Raw(ILOpCode.Ldc_i4, new IlInt32Operand(slot));
		Raw(ILOpCode.Call, new IlMethodOperand(callDispatch.ResolveTarget));
		Raw(ILOpCode.Calli, new IlCallSiteOperand(signature));
	}

	// ======================================================================================
	// raw emit

	/// <summary>
	/// Emits the given CIL instruction with no operand.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The opcode is canonicalized: passing a compact form such as <c>ldc.i4.s</c> emits, and is
	/// subsequently indistinguishable from, the corresponding long form. Prefix opcodes are rejected;
	/// a prefix is part of the instruction it applies to rather than an instruction of its own.
	/// </para>
	/// <para>
	/// Raw branch/switch instructions are not supported; use <see cref="Branch(ILOpCode, IlLabel)"/>.
	/// </para>
	/// </remarks>
	public void Raw(ILOpCode opCode) => builder.Emit(IlInstructionSpec.Raw(opCode));

	/// <summary>
	/// Emits the given CIL instruction with an operand.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The opcode is canonicalized: passing a compact form such as <c>ldc.i4.s</c> emits, and is
	/// subsequently indistinguishable from, the corresponding long form. Prefix opcodes are rejected;
	/// a prefix is part of the instruction it applies to rather than an instruction of its own.
	/// </para>
	/// <para>
	/// Raw branch/switch instructions are not supported; use <see cref="Branch(ILOpCode, IlLabel)"/>.
	/// </para>
	/// <para>
	/// No attempt is made to check if the operand makes an illegal reference to a reloadable mod (see
	/// <see cref="IlCollectibleReferenceException"/>'s type docs for more info); this does not affect
	/// correctness, but it does mean the failure surfaces later at JIT time rather than immediately.
	/// </para>
	/// </remarks>
	public void Raw(ILOpCode opCode, IlOperand operand) {
		ArgumentNullException.ThrowIfNull(operand);
		builder.Emit(IlInstructionSpec.Raw(opCode, operand));
	}

	// ======================================================================================
	// nop and basic control flow

	/// <summary>
	/// Emits the CIL <c>nop</c> instruction.
	/// </summary>
	public void Nop() => Raw(ILOpCode.Nop);

	/// <summary>
	/// Emits the CIL <c>ret</c> instruction.
	/// </summary>
	public void Ret() => Raw(ILOpCode.Ret);

	/// <summary>
	/// Emits the CIL <c>throw</c> instruction.
	/// </summary>
	public void Throw() => Raw(ILOpCode.Throw);

	/// <summary>
	/// Emits the CIL <c>rethrow</c> instruction.
	/// </summary>
	public void Rethrow() => Raw(ILOpCode.Rethrow);

	// ======================================================================================
	// basic stack ops

	/// <summary>
	/// Emits the CIL <c>dup</c> instruction.
	/// </summary>
	public void Dup() => Raw(ILOpCode.Dup);

	/// <summary>
	/// Emits the CIL <c>pop</c> instruction.
	/// </summary>
	public void Pop() => Raw(ILOpCode.Pop);

	/// <summary>
	/// Emits the CIL <c>ldnull</c> instruction.
	/// </summary>
	public void Ldnull() => Raw(ILOpCode.Ldnull);

	// ======================================================================================
	// loading literal values

	/// <summary>
	/// Emits the CIL <c>ldc.i4</c> instruction. The encoder may select an equivalent short form.
	/// </summary>
	public void LdcI4(int value) => Raw(ILOpCode.Ldc_i4, new IlInt32Operand(value));

	/// <summary>
	/// Emits the CIL <c>ldc.i8</c> instruction.
	/// </summary>
	public void LdcI8(long value) => Raw(ILOpCode.Ldc_i8, new IlInt64Operand(value));

	/// <summary>
	/// Emits the CIL <c>ldc.r4</c> instruction.
	/// </summary>
	public void LdcR4(float value) => Raw(ILOpCode.Ldc_r4, new IlFloat32Operand(value));

	/// <summary>
	/// Emits the CIL <c>ldc.r8</c> instruction.
	/// </summary>
	public void LdcR8(double value) => Raw(ILOpCode.Ldc_r8, new IlFloat64Operand(value));

	/// <summary>
	/// Emits the CIL <c>ldstr</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public void Ldstr(string value) => Raw(ILOpCode.Ldstr, new IlStringOperand(value ?? throw new ArgumentNullException(nameof(value))));

	// ======================================================================================
	// args

	/// <summary>
	/// Emits the CIL <c>ldarg</c> instruction. The encoder may select an equivalent short form.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldarg(int index) => Raw(ILOpCode.Ldarg, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Emits the CIL <c>ldarga</c> instruction. The encoder may select <c>ldarga.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldarga(int index) => Raw(ILOpCode.Ldarga, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Emits the CIL <c>starg</c> instruction. The encoder may select <c>starg.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Starg(int index) => Raw(ILOpCode.Starg, new IlArgumentOperand(validateIndex(index)));

	// ======================================================================================
	// locals

	/// <summary>
	/// Emits the CIL <c>ldloc</c> instruction. The encoder may select an equivalent short form.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldloc(int index) => Raw(ILOpCode.Ldloc, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Emits the CIL <c>ldloca</c> instruction. The encoder may select <c>ldloca.s</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldloca(int index) => Raw(ILOpCode.Ldloca, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Emits the CIL <c>stloc</c> instruction. The encoder may select an equivalent short form.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Stloc(int index) => Raw(ILOpCode.Stloc, new IlLocalOperand(validateIndex(index)));

	// ======================================================================================
	// fields

	/// <summary>
	/// Emits the CIL <c>ldfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="field"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Ldfld(IlFieldRef field) {
		ArgumentNullException.ThrowIfNull(field);
		IlTypeRestrictionCheck.AssertUnrestricted($"ldfld {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerContext));
		Raw(ILOpCode.Ldfld, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the CIL <c>ldflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="field"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Ldflda(IlFieldRef field) {
		ArgumentNullException.ThrowIfNull(field);
		IlTypeRestrictionCheck.AssertUnrestricted($"ldflda {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerContext));
		Raw(ILOpCode.Ldflda, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the CIL <c>stfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="field"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Stfld(IlFieldRef field) {
		ArgumentNullException.ThrowIfNull(field);
		IlTypeRestrictionCheck.AssertUnrestricted($"stfld {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerContext));
		Raw(ILOpCode.Stfld, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the CIL <c>ldsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="field"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Ldsfld(IlFieldRef field) {
		ArgumentNullException.ThrowIfNull(field);
		IlTypeRestrictionCheck.AssertUnrestricted($"ldsfld {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerContext));
		Raw(ILOpCode.Ldsfld, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the CIL <c>ldsflda</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="field"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Ldsflda(IlFieldRef field) {
		ArgumentNullException.ThrowIfNull(field);
		IlTypeRestrictionCheck.AssertUnrestricted($"ldsflda {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerContext));
		Raw(ILOpCode.Ldsflda, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the CIL <c>stsfld</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="field"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Stsfld(IlFieldRef field) {
		ArgumentNullException.ThrowIfNull(field);
		IlTypeRestrictionCheck.AssertUnrestricted($"stsfld {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerContext));
		Raw(ILOpCode.Stsfld, new IlFieldOperand(field));
	}

	// ======================================================================================
	// calls

	/// <summary>
	/// Emits the CIL <c>call</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="method"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Call(IlMethodRef method) {
		ArgumentNullException.ThrowIfNull(method);
		IlTypeRestrictionCheck.AssertUnrestricted($"call {method}", IlTypeRestrictionCheck.CheckMethodOperand(method, ownerContext));
		Raw(ILOpCode.Call, new IlMethodOperand(method));
	}

	/// <summary>
	/// Emits the CIL <c>callvirt</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="method"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Callvirt(IlMethodRef method) {
		ArgumentNullException.ThrowIfNull(method);
		IlTypeRestrictionCheck.AssertUnrestricted($"callvirt {method}", IlTypeRestrictionCheck.CheckMethodOperand(method, ownerContext));
		Raw(ILOpCode.Callvirt, new IlMethodOperand(method));
	}

	/// <summary>
	/// Emits the CIL <c>calli</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="signature"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="signature"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Calli(IlMethodSignature signature) {
		ArgumentNullException.ThrowIfNull(signature);
		IlTypeRestrictionCheck.AssertUnrestricted($"calli {signature}", IlTypeRestrictionCheck.CheckSignature(signature, ownerContext));
		Raw(ILOpCode.Calli, new IlCallSiteOperand(signature));
	}

	// ======================================================================================
	// object ops

	/// <summary>
	/// Emits the CIL <c>newobj</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="constructor"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="constructor"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Newobj(IlMethodRef constructor) {
		ArgumentNullException.ThrowIfNull(constructor);
		IlTypeRestrictionCheck.AssertUnrestricted($"newobj {constructor}", IlTypeRestrictionCheck.CheckMethodOperand(constructor, ownerContext));
		Raw(ILOpCode.Newobj, new IlMethodOperand(constructor));
	}

	/// <summary>
	/// Emits the CIL <c>box</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="type"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Box(IlTypeRef type) {
		ArgumentNullException.ThrowIfNull(type);
		IlTypeRestrictionCheck.AssertUnrestricted($"box {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerContext));
		Raw(ILOpCode.Box, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the CIL <c>unbox.any</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="type"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void UnboxAny(IlTypeRef type) {
		ArgumentNullException.ThrowIfNull(type);
		IlTypeRestrictionCheck.AssertUnrestricted($"unbox.any {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerContext));
		Raw(ILOpCode.Unbox_any, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the CIL <c>castclass</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="type"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Castclass(IlTypeRef type) {
		ArgumentNullException.ThrowIfNull(type);
		IlTypeRestrictionCheck.AssertUnrestricted($"castclass {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerContext));
		Raw(ILOpCode.Castclass, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the CIL <c>isinst</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="type"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Isinst(IlTypeRef type) {
		ArgumentNullException.ThrowIfNull(type);
		IlTypeRestrictionCheck.AssertUnrestricted($"isinst {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerContext));
		Raw(ILOpCode.Isinst, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the CIL <c>newarr</c> instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="type"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Newarr(IlTypeRef type) {
		ArgumentNullException.ThrowIfNull(type);
		IlTypeRestrictionCheck.AssertUnrestricted($"newarr {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerContext));
		Raw(ILOpCode.Newarr, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the CIL <c>ldtoken</c> instruction for a type.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="type"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Ldtoken(IlTypeRef type) {
		ArgumentNullException.ThrowIfNull(type);
		IlTypeRestrictionCheck.AssertUnrestricted($"ldtoken {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerContext));
		Raw(ILOpCode.Ldtoken, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the CIL <c>ldtoken</c> instruction for a method.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="method"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Ldtoken(IlMethodRef method) {
		ArgumentNullException.ThrowIfNull(method);
		IlTypeRestrictionCheck.AssertUnrestricted($"ldtoken {method}", IlTypeRestrictionCheck.CheckMethodOperand(method, ownerContext));
		Raw(ILOpCode.Ldtoken, new IlMethodOperand(method));
	}

	/// <summary>
	/// Emits the CIL <c>ldtoken</c> instruction for a field.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="field"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	public void Ldtoken(IlFieldRef field) {
		ArgumentNullException.ThrowIfNull(field);
		IlTypeRestrictionCheck.AssertUnrestricted($"ldtoken {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerContext));
		Raw(ILOpCode.Ldtoken, new IlFieldOperand(field));
	}

	// ======================================================================================
	// branches

	/// <summary>
	/// Emits a branch opcode targeting a transaction-scoped label.
	/// </summary>
	public void Branch(ILOpCode opCode, IlLabel target) => builder.Emit(IlInstructionSpec.Branch(opCode, target));

	/// <summary>
	/// Emits the CIL <c>br</c> instruction.
	/// </summary>
	public void Br(IlLabel target) => Branch(ILOpCode.Br, target);

	/// <summary>
	/// Emits the CIL <c>brtrue</c> instruction.
	/// </summary>
	public void Brtrue(IlLabel target) => Branch(ILOpCode.Brtrue, target);

	/// <summary>
	/// Emits the CIL <c>brfalse</c> instruction.
	/// </summary>
	public void Brfalse(IlLabel target) => Branch(ILOpCode.Brfalse, target);

	/// <summary>
	/// Emits the CIL <c>beq</c> instruction.
	/// </summary>
	public void Beq(IlLabel target) => Branch(ILOpCode.Beq, target);

	/// <summary>
	/// Emits the CIL <c>bne.un</c> instruction.
	/// </summary>
	public void BneUn(IlLabel target) => Branch(ILOpCode.Bne_un, target);

	/// <summary>
	/// Emits the CIL <c>leave</c> instruction.
	/// </summary>
	public void Leave(IlLabel target) => Branch(ILOpCode.Leave, target);

	/// <summary>
	/// Emits the CIL <c>switch</c> instruction targeting the supplied labels.
	/// </summary>
	public void Switch(ReadOnlySpan<IlLabel> targets) => builder.Emit(IlInstructionSpec.Switch(targets));

	// ======================================================================================
	// helper methods
	private static int validateIndex(int index) {
		if ((uint)index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return index;
	}
}
