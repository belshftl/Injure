// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Modif.Detours;

internal sealed class DetourTransform : IDetourTransform {
	private readonly Func<MethodIdentity, MethodBase> resolveMethod;
	private readonly Dictionary<MethodIdentity, int> slots = new();
	private readonly Lock slotsLock = new();

	private static readonly IlMethodRef enterAndCheckRef = IlRefFactory.Method(
		typeof(DetourDispatch).GetMethod(nameof(DetourDispatch.EnterAndCheck), BindingFlags.Static | BindingFlags.Public)!
	);
	private static readonly IlMethodRef getChainEntryRef = IlRefFactory.Method(
		typeof(DetourDispatch).GetMethod(nameof(DetourDispatch.GetChainEntry), BindingFlags.Static | BindingFlags.Public)!
	);

	/// <param name="resolveMethod">
	/// Converts a method identity into a reflection <see cref="MethodBase"/> for building thunks.
	/// The abstractions operate in tokens, but generating thunks needs reflection.
	/// </param>
	public DetourTransform(Func<MethodIdentity, MethodBase> resolveMethod) {
		ArgumentNullException.ThrowIfNull(resolveMethod);
		this.resolveMethod = resolveMethod;
	}

	// ==========================================================================================
	// chains
	public void UpdateChain(MethodIdentity method, ImmutableArray<DetourRegistration> detours) {
		int slot = slotFor(method);
		if (detours.IsDefaultOrEmpty) {
			DetourDispatch.ClearChain(slot);
			return;
		}

		var chain = DetourChain.Build(resolveMethod(method), slot, detours);
		DetourDispatch.SetChain(slot, chain.Entry, chain.State);
	}

	public bool HasChain(MethodIdentity method) {
		lock (slotsLock)
			return slots.ContainsKey(method);
	}

	private int slotFor(MethodIdentity method) {
		lock (slotsLock) {
			if (!slots.TryGetValue(method, out int slot)) {
				slot = DetourDispatch.AllocateSlot();
				slots[method] = slot;
			}
			return slot;
		}
	}

	// ==========================================================================================
	// prologue

	/// <summary>
	/// Prepends the detour prologue to a body.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Something like this is emitted:
	/// <code>
	///     ldc.i4    &lt;slot&gt;
	///     call      bool DetourDispatch::EnterAndCheck(int32)
	///     brfalse   original
	///     ldarg.0 ... ldarg n
	///     ldc.i4    &lt;slot&gt;
	///     call      native int DetourDispatch::GetChainEntry(int32)
	///     calli     &lt;the target's signature, as a static one&gt;
	///     ret
	/// original:
	///     &lt;the patched body, untouched&gt;
	/// </code>
	/// In other words, if this entry should run the chain, push the arguments, fetch the chain
	/// entry, and call it; otherwise, fall through to the original body.
	/// </para>
	/// <para>
	/// Emitting at boundary zero puts the prologue before the anchor of the first instruction, so
	/// it lands outside any protected region that starts at the top of the method, and every existing
	/// branch to the first instruction still points to the original body rather than the prologue.
	/// </para>
	/// </remarks>
	public IlMethodBody Apply(
		MethodIdentity method,
		IlMethodBody body,
		ImmutableArray<DetourRegistration> detours
	) {
		InternalStateException.ThrowIfNull(body);
		int slot = slotFor(method);
		IlMethodSignature signature = chainSignature(body.Method);

		IlTransactionCore core = new(body, EngineInfo.OwnerId, null, default, null);
		IlLabel original = core.DefineLabel();

		// note: DetourReentrancyTests in the test project mirrors this pattern, if this changes
		// it must be updated accordingly
		core.EmitAtBoundary(0, e => {
			e.LdcI4(slot);
			e.Call(enterAndCheckRef);
			e.Brfalse(original);
			for (int arg = 0; arg < signature.ParameterTypes.Length; arg++)
				e.Ldarg(arg);
			e.LdcI4(slot);
			e.Call(getChainEntryRef);
			e.Calli(signature);
			e.Ret();
			e.MarkLabel(original);
		});
		core.Commit();

		return body;
	}

	private static IlMethodSignature chainSignature(IlMethodRef target) {
		IlMethodSignature signature = target.Signature;
		ImmutableArray<IlTypeRef> parameters = signature.HasThis && !signature.ExplicitThis
			? [target.DeclaringType, .. signature.ParameterTypes]
			: signature.ParameterTypes;
		return new IlMethodSignature(
			SignatureCallingConvention.Default,
			hasThis: false,
			explicitThis: false,
			genericParameterCount: 0,
			requiredParameterCount: parameters.Length,
			signature.ReturnType,
			parameters
		);
	}
}
