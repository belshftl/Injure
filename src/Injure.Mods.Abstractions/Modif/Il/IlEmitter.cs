// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Builder for one emitted IL fragment.
/// </summary>
/// <remarks>
/// <para>
/// The emitter emits canonical instructions only (see <c>docs/mods/canonical-short-form.md</c>).
/// When a full new method body is being written in, it gets shortened by the encoder.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly ref struct IlEmitter {
	private IlFragmentBuilder builder => field ?? throw new InvalidOperationException("this IlEmitter value is uninitialized/invalid");
	private readonly IlOwnerInfo ownerInfo;
	private readonly IIlCallDispatch? callDispatch;

	internal IlEmitter(IlFragmentBuilder builder, IlOwnerInfo ownerInfo, IIlCallDispatch? callDispatch) {
		InternalStateException.ThrowIfNull(builder);
		this.builder = builder;
		this.ownerInfo = ownerInfo;
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
	/// table rather than by direct reference. This is the only way to call into a reloadable mod,
	/// and also allows emitting calls to non-public mod methods.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="method"/> is an instance method, or if it has no callable entry
	/// point: open generic, abstract, or no CIL body + no other callable stub (P/Invoke impl,
	/// <c>InternalCall</c>/<c>Runtime</c>, <c>[UnsafeAccessor]</c>).
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="method"/>'s <b>signature</b> would create an illegal reference to a
	/// reloadable mod; see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Takes in a reflection method rather than a structural metadata reference because the target has
	/// to already be callable now rather than simply be fed into a metadata encoder.
	/// </para>
	/// <para>
	/// The sequence emitted is:
	/// <code>
	/// ldc.i4 &lt;...&gt;
	/// call &lt;...&gt;
	/// calli &lt;signature&gt;
	/// </code>
	/// Never silently emits a direct call instead. The caller pushes arguments exactly as for
	/// <c>call</c>. The emitted opcodes are part of the API, for IL-matching purposes, but the
	/// operands of the <c>ldc.i4</c> and <c>call</c> are currently not, and should currently be
	/// treated as opaque magic values; that will be revisited before a first stable release.
	/// <c>&lt;signature&gt;</c> is the signature of <paramref name="method"/>.
	/// </para>
	/// <para>
	/// Usable in a non-reloadable mod too. The cost goes from one direct call to a volatile table
	/// read + an indirect call. In practice, the usual cost is just that the call can't be inlined
	/// anymore; the rest is usually negligible. Benchmark your case if it's relevant.
	/// </para>
	/// <para>
	/// See <see cref="MgroupIndirectCall{TDelegate}(TDelegate)"/> for a convenience wrapper.
	/// </para>
	/// </remarks>
	public void IndirectCall(MethodInfo method) {
		if (callDispatch is null)
			throw new InternalStateException("no indirect call dispatch mechanism available here; if this is in an engine test, pass a nonnull IIlCallDispatch to your IlTransactionCore construction");

		ArgumentNullException.ThrowIfNull(method);
		if (!method.IsStatic)
			throw new ArgumentException("method must be static", nameof(method));
		if (method.IsAbstract)
			throw new ArgumentException("method is abstract and, as such, has no callable entry point", nameof(method));
		if (method.IsGenericMethodDefinition || method.DeclaringType?.IsGenericTypeDefinition == true)
			throw new ArgumentException("method is an open generic / on an open generic type and, as such, has no callable entry point", nameof(method));
		if (method.IsGenericMethodDefinition || method.DeclaringType?.IsGenericTypeDefinition == true)
			throw new ArgumentException("method is an open generic / on an open generic type and, as such, has no callable entry point", nameof(method));
		if (
			method.GetMethodBody() is null &&
			(method.Attributes & MethodAttributes.PinvokeImpl) == 0 &&
			(method.GetMethodImplementationFlags() & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) == 0 &&
			!method.IsDefined(typeof(UnsafeAccessorAttribute), inherit: false)
		)
			throw new ArgumentException("method is a method with no CIL body, P/Invoke impl, runtime management flags, or [UnsafeAccessor]", nameof(method));

		IlMethodSignature signature = IlRefFactory.Method(method).Signature;
		IlTypeRestrictionCheck.AssertUnrestricted($"calli {signature}", IlTypeRestrictionCheck.CheckSignature(signature, ownerInfo));
		int slot = callDispatch.AllocateSlot(method);
		RawNonbranch(ILOpCode.Ldc_i4, new IlInt32Operand(slot));
		RawNonbranch(ILOpCode.Call, new IlMethodOperand(callDispatch.ResolveTarget));
		RawNonbranch(ILOpCode.Calli, new IlCallSiteOperand(signature));
	}

	// ======================================================================================
	// mgroup call wrappers

	/// <summary>
	/// Like <see cref="Call(IlMethodRef)"/>, but takes a static method group.
	/// </summary>
	/// <typeparam name="TDelegate">
	/// Can be explicitly specified to pick the overload of an overloaded method group.
	/// </typeparam>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="mgroup"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="mgroup"/> isn't a single static method group, or is inaccessible from
	/// the patched method (static local functions fail solely because of the accessibility check).
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="mgroup"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The delegate is only used to extract the target method and is then discarded. This is
	/// <b>not</b> the same as, say, MonoMod's <c>EmitDelegate</c>, which retains the delegate
	/// internally if it needs to. This is an intentional design point: needing to emit a call that
	/// captures a closure is nearly always a bug, and more often happens due to a misunderstanding of
	/// how the API works than on purpose (example: <c>EmitDelegate(Singleton.Instance.Method)</c>
	/// captures the instance when the intent is most likely to have the receiver be pushed to the
	/// stack by other IL).
	/// </para>
	/// <para>
	/// Only a single static method group is accepted. Instance methods, lambdas (<b>including static
	/// lambdas</b>, as they compile to instance methods too), local functions, extension methods
	/// closed over a receiver, and other kinds of closures are rejected. Static local functions are
	/// fine in principle, but don't work in practice as they compile to inaccessible methods. An
	/// overloaded method group has no natural single delegate type, so
	/// <typeparamref name="TDelegate"/> has to be specified explicitly for those.
	/// </para>
	/// <para>
	/// See <see cref="Call(IlMethodRef)"/> for more info, including accessibility/reloadable-mod
	/// caveats; this is just a convenience wrapper, and the actual behavior is detailed there.
	/// </para>
	/// </remarks>
	public void MgroupCall<TDelegate>(TDelegate mgroup) where TDelegate : Delegate {
		MethodInfo method = requireStaticMgroup(mgroup, nameof(mgroup));
		if (!isAccessibleFromAnywhere(method))
			throw new ArgumentException(
				$"'{method.DeclaringType}::{method.Name}' is non-public (or is on a non-public type), so the patched method wouldn't be able to call it directly; consider using MgroupIndirectCall, which isn't subject to access checks",
				nameof(mgroup)
			);
		Call(IlRefFactory.Method(method));
	}

	/// <summary>
	/// Like <see cref="IndirectCall(MethodInfo)"/>, but takes a static method group.
	/// </summary>
	/// <typeparam name="TDelegate">
	/// Can be explicitly specified to pick the overload of an overloaded method group.
	/// </typeparam>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="mgroup"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="mgroup"/> isn't a single static method group or static local function.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="mgroup"/>'s <b>signature</b> would create an illegal reference to a
	/// reloadable mod; see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The delegate is only used to extract the target method and is then discarded. This is
	/// <b>not</b> the same as, say, MonoMod's <c>EmitDelegate</c>, which retains the delegate
	/// internally if it needs to. This is an intentional design point: needing to emit a call that
	/// captures a closure is nearly always a bug, and more often happens due to a misunderstanding of
	/// how the API works than on purpose (example: <c>EmitDelegate(Singleton.Instance.Method)</c>
	/// captures the instance when the intent is most likely to have the receiver be pushed to the
	/// stack by other IL).
	/// </para>
	/// <para>
	/// Only a single static method group or static local function is accepted. Instance methods,
	/// lambdas (<b>including static lambdas</b>, as they compile to instance methods too), local
	/// functions, extension methods closed over a receiver, and other kinds of closures are rejected.
	/// An overloaded method group has no natural single delegate type, so
	/// <typeparamref name="TDelegate"/> has to be specified explicitly for those.
	/// </para>
	/// <para>
	/// See <see cref="IndirectCall(MethodInfo)"/> for more info; this is just a convenience wrapper,
	/// and the actual behavior is detailed there.
	/// </para>
	/// </remarks>
	public void MgroupIndirectCall<TDelegate>(TDelegate mgroup) where TDelegate : Delegate =>
		IndirectCall(requireStaticMgroup(mgroup, nameof(mgroup)));

	private static MethodInfo requireStaticMgroup(Delegate mgroup, string paramName) {
		ArgumentNullException.ThrowIfNull(mgroup, paramName);
		if (!mgroup.HasSingleTarget)
			throw new ArgumentException("the delegate combines several methods; pass a single static method group", paramName);

		MethodInfo method = mgroup.Method;
		if (method is DynamicMethod)
			throw new ArgumentException("the delegate is to a dynamic method, which doesn't have any metadata for emitted IL to reference and would need delegate retention", paramName);
		if (!method.IsStatic || mgroup.Target is not null) {
			string hint = method.DeclaringType?.IsDefined(typeof(CompilerGeneratedAttribute), false) == true
				? ". it looks like a lambda, which compiles to an instance method even if it's a static lambda; use a regular static method instead" 
				: "";
			throw new ArgumentException(
				$"'{method.DeclaringType}::{method.Name}' must be a regular static method group; lambdas, instance methods, or other kinds of closures would need delegate retention{hint}",
				paramName
			);
		}
		return method;
	}

	private static bool isAccessibleFromAnywhere(MethodInfo method) {
		if (!method.IsPublic)
			return false;
		for (Type? type = method.DeclaringType; type is not null; type = type.DeclaringType)
			if (!(type.IsPublic || type.IsNestedPublic))
				return false;
		if (method.IsGenericMethod && !method.ContainsGenericParameters)
			foreach (Type type in method.GetGenericArguments())
				if (!(type.IsPublic || type.IsNestedPublic))
					return false;
		return true;
	}

	// ======================================================================================
	// raw emit

	/// <summary>
	/// Emits the given canonical non-branch instruction, with no operand. Use of this method is
	/// discouraged in favor of the per-opcode methods.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="opCode"/> isn't a defined opcode, is a prefix, is a compact encoding,
	/// is a branch or <c>switch</c>, or takes an operand.
	/// </exception>
	/// <remarks>
	/// <para>
	/// <b>The opcode must be canonical.</b> Compact encodings such as <c>ldc.i4.s</c> are rejected.
	/// Prefix opcodes are rejected too; a prefix is part of the instruction it applies to rather than
	/// an instruction of its own. Emission of instructions with prefixes is curretly unimplemented.
	/// </para>
	/// <para>
	/// See <see cref="RawBranch(ILOpCode, IlLabel)"/> for branches. <c>switch</c> is unsupported here;
	/// see <see cref="Switch(ReadOnlySpan{IlLabel})"/>. There is no other way to emit <c>switch</c>.
	/// </para>
	/// </remarks>
	public void RawNonbranch(ILOpCode opCode) => builder.Emit(IlInstructionSpec.RawNonbranch(opCode));

	/// <summary>
	/// Emits the given canonical non-branch instruction, with an operand. Use of this method is
	/// discouraged in favor of the per-opcode methods.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="operand"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="operand"/> is an argument or local index that doesn't fit into a 16-bit
	/// unsigned integer.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="opCode"/> isn't a defined opcode, is a prefix, is a compact encoding, or
	/// is a branch or <c>switch</c>, or if <paramref name="operand"/> isn't of the kind the opcode takes.
	/// </exception>
	/// <remarks>
	/// <para>
	/// <b>The opcode must be canonical.</b> Compact encodings such as <c>ldc.i4.s</c> are rejected.
	/// Prefix opcodes are rejected too; a prefix is part of the instruction it applies to rather than
	/// an instruction of its own. Emission of instructions with prefixes is curretly unimplemented.
	/// </para>
	/// <para>
	/// See <see cref="RawBranch(ILOpCode, IlLabel)"/> for branches. <c>switch</c> is unsupported here;
	/// see <see cref="Switch(ReadOnlySpan{IlLabel})"/>. There is no other way to emit <c>switch</c>.
	/// </para>
	/// <para>
	/// No attempt is made to check if the operand makes an illegal reference to a reloadable mod (see
	/// <see cref="IlCollectibleReferenceException"/>'s type docs for more info); this does not affect
	/// correctness, but it does mean the failure surfaces later at JIT time rather than immediately.
	/// </para>
	/// </remarks>
	public void RawNonbranch(ILOpCode opCode, IlOperand operand) {
		ArgumentNullException.ThrowIfNull(operand);
		builder.Emit(IlInstructionSpec.RawNonbranch(opCode, operand));
	}

	/// <summary>
	/// Emits a canonical branch instruction. Use of this method is discouraged in favor of the
	/// per-opcode methods.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The opcode must be canonical.</b> Compact encodings such as <c>br.s</c> are rejected. Prefix
	/// opcodes are rejected too; a prefix is part of the instruction it applies to rather than an
	/// instruction of its own. Emission of instructions with prefixes is curretly unimplemented.
	/// </para>
	/// <para>
	/// <c>switch</c> is unsupported here; see <see cref="Switch(ReadOnlySpan{IlLabel})"/>. There is no
	/// other way to emit <c>switch</c>.
	/// </para>
	/// </remarks>
	public void RawBranch(ILOpCode opCode, IlLabel target) =>
		builder.Emit(IlInstructionSpec.RawBranch(opCode, target));

	// ======================================================================================
	// nop and basic control flow

	/// <summary>
	/// Emits the <c>nop</c> canonical instruction.
	/// </summary>
	public void Nop() => RawNonbranch(ILOpCode.Nop);

	/// <summary>
	/// Emits the <c>ret</c> canonical instruction.
	/// </summary>
	public void Ret() => RawNonbranch(ILOpCode.Ret);

	/// <summary>
	/// Emits the <c>throw</c> canonical instruction.
	/// </summary>
	public void Throw() => RawNonbranch(ILOpCode.Throw);

	/// <summary>
	/// Emits the <c>rethrow</c> canonical instruction.
	/// </summary>
	public void Rethrow() => RawNonbranch(ILOpCode.Rethrow);

	// ======================================================================================
	// basic stack ops

	/// <summary>
	/// Emits the <c>dup</c> canonical instruction.
	/// </summary>
	public void Dup() => RawNonbranch(ILOpCode.Dup);

	/// <summary>
	/// Emits the <c>pop</c> canonical instruction.
	/// </summary>
	public void Pop() => RawNonbranch(ILOpCode.Pop);

	/// <summary>
	/// Emits the <c>ldnull</c> canonical instruction.
	/// </summary>
	public void Ldnull() => RawNonbranch(ILOpCode.Ldnull);

	// ======================================================================================
	// loading literal values

	/// <summary>
	/// Emits the <c>ldc.i4</c> canonical instruction.
	/// </summary>
	public void LdcI4(int value) => RawNonbranch(ILOpCode.Ldc_i4, new IlInt32Operand(value));

	/// <summary>
	/// Emits the <c>ldc.i8</c> canonical instruction.
	/// </summary>
	public void LdcI8(long value) => RawNonbranch(ILOpCode.Ldc_i8, new IlInt64Operand(value));

	/// <summary>
	/// Emits the <c>ldc.r4</c> canonical instruction.
	/// </summary>
	public void LdcR4(float value) => RawNonbranch(ILOpCode.Ldc_r4, new IlFloat32Operand(value));

	/// <summary>
	/// Emits the <c>ldc.r8</c> canonical instruction.
	/// </summary>
	public void LdcR8(double value) => RawNonbranch(ILOpCode.Ldc_r8, new IlFloat64Operand(value));

	/// <summary>
	/// Emits the <c>ldstr</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public void Ldstr(string value) => RawNonbranch(ILOpCode.Ldstr, new IlStringOperand(value ?? throw new ArgumentNullException(nameof(value))));

	// ======================================================================================
	// args

	/// <summary>
	/// Emits the <c>ldarg</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldarg(int index) => RawNonbranch(ILOpCode.Ldarg, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Emits the <c>ldarga</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldarga(int index) => RawNonbranch(ILOpCode.Ldarga, new IlArgumentOperand(validateIndex(index)));

	/// <summary>
	/// Emits the <c>starg</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Starg(int index) => RawNonbranch(ILOpCode.Starg, new IlArgumentOperand(validateIndex(index)));

	// ======================================================================================
	// locals

	/// <summary>
	/// Emits the <c>ldloc</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldloc(int index) => RawNonbranch(ILOpCode.Ldloc, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Emits the <c>ldloca</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Ldloca(int index) => RawNonbranch(ILOpCode.Ldloca, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Emits the <c>stloc</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="index"/> doesn't fit into a 16-bit unsigned integer.
	/// </exception>
	public void Stloc(int index) => RawNonbranch(ILOpCode.Stloc, new IlLocalOperand(validateIndex(index)));

	/// <summary>
	/// Emits the <c>ldloc</c> canonical instruction.
	/// </summary>
	public void Ldloc(IlLocal local) => RawNonbranch(ILOpCode.Ldloc, new IlLocalOperand(builder.ValidateLocal(local)));

	/// <summary>
	/// Emits the <c>ldloca</c> canonical instruction.
	/// </summary>
	public void Ldloca(IlLocal local) => RawNonbranch(ILOpCode.Ldloca, new IlLocalOperand(builder.ValidateLocal(local)));

	/// <summary>
	/// Emits the <c>stloc</c> canonical instruction.
	/// </summary>
	public void Stloc(IlLocal local) => RawNonbranch(ILOpCode.Stloc, new IlLocalOperand(builder.ValidateLocal(local)));

	// ======================================================================================
	// fields

	/// <summary>
	/// Emits the <c>ldfld</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"ldfld {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerInfo));
		RawNonbranch(ILOpCode.Ldfld, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the <c>ldflda</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"ldflda {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerInfo));
		RawNonbranch(ILOpCode.Ldflda, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the <c>stfld</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"stfld {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerInfo));
		RawNonbranch(ILOpCode.Stfld, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the <c>ldsfld</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"ldsfld {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerInfo));
		RawNonbranch(ILOpCode.Ldsfld, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the <c>ldsflda</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"ldsflda {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerInfo));
		RawNonbranch(ILOpCode.Ldsflda, new IlFieldOperand(field));
	}

	/// <summary>
	/// Emits the <c>stsfld</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"stsfld {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerInfo));
		RawNonbranch(ILOpCode.Stsfld, new IlFieldOperand(field));
	}

	// ======================================================================================
	// calls

	/// <summary>
	/// Emits the <c>call</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="method"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	/// <remarks>
	/// <para>
	/// <b>Use <see cref="IndirectCall(MethodInfo)"/> instead if <paramref name="method"/> is in a
	/// reloadable mod</b> and you're patching the engine/game, because the emitted IL lives in the
	/// engine/game's non-collectible assembly, which can't directly reference a collectible one.
	/// </para>
	/// <para>
	/// In addition to the reloadable-mod restriction, since a call to <paramref name="method"/> is,
	/// quite literally, injected into the patched method, <paramref name="method"/> must be accessible
	/// from where the patched method lives. If that is not the case, the next call to the patched
	/// method fails with <see cref="MethodAccessException"/>. In practice, this usually means
	/// <paramref name="method"/> must be public, be on a publicly accessible type, and (if applicable)
	/// have publicly accessible generic arguments. <see cref="IndirectCall(MethodInfo)"/> has no such
	/// restriction regarding accessibility, since the call goes through a function pointer instead
	/// of a metadata reference.
	/// </para>
	/// <para>
	/// See <see cref="MgroupCall{TDelegate}(TDelegate)"/> for a convenience wrapper. Said wrapper also
	/// checks for correct accessibility ahead of time (which isn't possible here;
	/// <see cref="IlMethodRef"/> doesn't encode accessibility.)
	/// </para>
	/// </remarks>
	public void Call(IlMethodRef method) {
		ArgumentNullException.ThrowIfNull(method);
		IlTypeRestrictionCheck.AssertUnrestricted($"call {method}", IlTypeRestrictionCheck.CheckMethodOperand(method, ownerInfo));
		RawNonbranch(ILOpCode.Call, new IlMethodOperand(method));
	}

	/// <summary>
	/// Emits the <c>callvirt</c> canonical instruction.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="IlCollectibleReferenceException">
	/// Thrown if <paramref name="method"/> would create an illegal reference to a reloadable mod;
	/// see <see cref="IlCollectibleReferenceException"/>'s type docs for more info.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The reloadable-mod and accessibility restrictions of <see cref="Call(IlMethodRef)"/> apply here
	/// too, and concern <paramref name="method"/> as named: its declaring type, signature, and generic
	/// arguments. Where the call dispatches to at runtime doesn't matter. A <c>callvirt</c> to a virtual
	/// or interface method declared by the engine, the game, or the BCL is fine even when the receiver
	/// turns out to be a reloadable mod's type that overrides or implements it, since the call goes
	/// through the receiver's vtable rather than a metadata reference to the mod.
	/// </para>
	/// <para>
	/// There's no indirect counterpart. If you do genuinely need to call an instance method from a
	/// reloadable mod, make a static wrapper method that takes in the receiver, and call that with
	/// <see cref="IndirectCall(MethodInfo)"/>.
	/// </para>
	/// <para>
	/// Prefix emission is planned before a stable release but is not implemented yet, so
	/// <c>constrained.</c> is currently unavailable.
	/// </para>
	/// </remarks>
	public void Callvirt(IlMethodRef method) {
		ArgumentNullException.ThrowIfNull(method);
		IlTypeRestrictionCheck.AssertUnrestricted($"callvirt {method}", IlTypeRestrictionCheck.CheckMethodOperand(method, ownerInfo));
		RawNonbranch(ILOpCode.Callvirt, new IlMethodOperand(method));
	}

	/// <summary>
	/// Emits the <c>calli</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"calli {signature}", IlTypeRestrictionCheck.CheckSignature(signature, ownerInfo));
		RawNonbranch(ILOpCode.Calli, new IlCallSiteOperand(signature));
	}

	// ======================================================================================
	// object ops

	/// <summary>
	/// Emits the <c>newobj</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"newobj {constructor}", IlTypeRestrictionCheck.CheckMethodOperand(constructor, ownerInfo));
		RawNonbranch(ILOpCode.Newobj, new IlMethodOperand(constructor));
	}

	/// <summary>
	/// Emits the <c>box</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"box {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerInfo));
		RawNonbranch(ILOpCode.Box, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the <c>unbox.any</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"unbox.any {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerInfo));
		RawNonbranch(ILOpCode.Unbox_any, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the <c>castclass</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"castclass {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerInfo));
		RawNonbranch(ILOpCode.Castclass, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the <c>isinst</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"isinst {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerInfo));
		RawNonbranch(ILOpCode.Isinst, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the <c>newarr</c> canonical instruction.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"newarr {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerInfo));
		RawNonbranch(ILOpCode.Newarr, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the <c>ldtoken</c> canonical instruction for a type.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"ldtoken {type}", IlTypeRestrictionCheck.CheckTypeOperand(type, ownerInfo));
		RawNonbranch(ILOpCode.Ldtoken, new IlTypeOperand(type));
	}

	/// <summary>
	/// Emits the <c>ldtoken</c> canonical instruction for a method.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"ldtoken {method}", IlTypeRestrictionCheck.CheckMethodOperand(method, ownerInfo));
		RawNonbranch(ILOpCode.Ldtoken, new IlMethodOperand(method));
	}

	/// <summary>
	/// Emits the <c>ldtoken</c> canonical instruction for a field.
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
		IlTypeRestrictionCheck.AssertUnrestricted($"ldtoken {field}", IlTypeRestrictionCheck.CheckFieldOperand(field, ownerInfo));
		RawNonbranch(ILOpCode.Ldtoken, new IlFieldOperand(field));
	}

	// ======================================================================================
	// branches/switch

	/// <summary>
	/// Emits the <c>br</c> canonical instruction.
	/// </summary>
	public void Br(IlLabel target) => RawBranch(ILOpCode.Br, target);

	/// <summary>
	/// Emits the <c>brtrue</c> canonical instruction.
	/// </summary>
	public void Brtrue(IlLabel target) => RawBranch(ILOpCode.Brtrue, target);

	/// <summary>
	/// Emits the <c>brfalse</c> canonical instruction.
	/// </summary>
	public void Brfalse(IlLabel target) => RawBranch(ILOpCode.Brfalse, target);

	/// <summary>
	/// Emits the <c>beq</c> canonical instruction.
	/// </summary>
	public void Beq(IlLabel target) => RawBranch(ILOpCode.Beq, target);

	/// <summary>
	/// Emits the <c>bne.un</c> canonical instruction.
	/// </summary>
	public void BneUn(IlLabel target) => RawBranch(ILOpCode.Bne_un, target);

	/// <summary>
	/// Emits the <c>leave</c> canonical instruction.
	/// </summary>
	public void Leave(IlLabel target) => RawBranch(ILOpCode.Leave, target);

	/// <summary>
	/// Emits the <c>switch</c> canonical instruction targeting the supplied labels.
	/// </summary>
	/// <remarks>
	/// This is the only way to emit <c>switch</c>; raw-emit APIs do not support it.
	/// </remarks>
	public void Switch(ReadOnlySpan<IlLabel> targets) => builder.Emit(IlInstructionSpec.Switch(targets));

	// ======================================================================================
	// helper methods
	private static int validateIndex(int index) {
		if ((uint)index > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(index));
		return index;
	}
}
