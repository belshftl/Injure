// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Mods.Abstractions.Tests.MethodModification.Il;

[ModLifetimeIdentityBelongsTo(IlTest.OwnerId)]
internal readonly struct TestL : IModLifetimeIdentity;

internal static class IlTest {
	public const string OwnerId = "test";
	public const string LocalId = "patch";

	public static IlTypeScope Scope { get; } = new IlTypeScope.Module(new IlModuleIdentity(Guid.Empty, "Test"));

	public static IlTypeRef Void { get; } = new IlPrimitiveTypeRef(PrimitiveTypeCode.Void);
	public static IlTypeRef Int32 { get; } = new IlPrimitiveTypeRef(PrimitiveTypeCode.Int32);
	public static IlTypeRef Object { get; } = Named("System", "Object");
	public static IlTypeRef Exception { get; } = Named("System", "Exception");

	public static IlNamedTypeRef Named(string ns, string name) =>
		new(Scope, null, ns, name, 0, IlNamedTypeKind.Class);

	/// <summary>
	/// A static signature; every parameter is required.
	/// </summary>
	public static IlMethodSignature Sig(IlTypeRef returnType, params IlTypeRef[] parameters) =>
		new(SignatureCallingConvention.Default, false, false, 0, parameters.Length, returnType, [.. parameters]);

	/// <summary>
	/// An instance signature with an implicit receiver.
	/// </summary>
	public static IlMethodSignature InstanceSig(IlTypeRef returnType, params IlTypeRef[] parameters) =>
		new(SignatureCallingConvention.Default, true, false, 0, parameters.Length, returnType, [.. parameters]);

	/// <summary>
	/// An instance signature whose receiver is the first entry of the parameter list.
	/// </summary>
	public static IlMethodSignature ExplicitThisSig(IlTypeRef returnType, params IlTypeRef[] parameters) =>
		new(SignatureCallingConvention.Default, true, true, 0, parameters.Length, returnType, [.. parameters]);

	/// <summary>
	/// A vararg signature; <paramref name="required"/> splits required params from optional ones.
	/// </summary>
	public static IlMethodSignature VarargSig(IlTypeRef returnType, int required, params IlTypeRef[] parameters) =>
		new(SignatureCallingConvention.VarArgs, false, false, 0, required, returnType, [.. parameters]);

	public static IlMethodRef Method(string name, IlMethodSignature signature) => new(Object, name, signature, []);

	public static IlMethodRef Method(string name, IlTypeRef returnType, params IlTypeRef[] parameters) =>
		Method(name, Sig(returnType, parameters));
}

/// <summary>
/// Builds an arbitrary method body. Said body does not have to be valid.
/// </summary>
/// <remarks>
/// Anchors are assigned by position, so boundary <c>n</c> always has anchor ID <c>n + 1</c> and a
/// branch can be written before its target exists.
/// </remarks>
internal sealed class BodyBuilder {
	private readonly List<IlInstruction> instrs = [];
	private readonly List<IlExceptionRegion> regions = [];
	private ulong nextId = 1;

	/// <summary>
	/// The boundary a subsequently added instruction will occupy.
	/// </summary>
	public int Next => instrs.Count;

	/// <summary>
	/// The anchor naming a boundary, valid before the boundary exists.
	/// </summary>
	public static IlAnchorId Anchor(int boundary) => new((ulong)(boundary + 1));

	public BodyBuilder Add(ILOpCode opCode, IlOperand? operand = null, IlInstructionPrefixes? prefixes = null) {
		instrs.Add(new IlInstruction(
			new IlInstructionId(nextId++),
			opCode,
			operand ?? IlNoneOperand.Instance,
			prefixes,
			IlInstruction.NoOriginalOffset,
			default
		));
		return this;
	}

	public BodyBuilder Nop() => Add(ILOpCode.Nop);
	public BodyBuilder Pop() => Add(ILOpCode.Pop);
	public BodyBuilder Ret() => Add(ILOpCode.Ret);
	public BodyBuilder LdcI4(int value) => Add(ILOpCode.Ldc_i4, new IlInt32Operand(value));
	public BodyBuilder Ldarg(int index) => Add(ILOpCode.Ldarg, new IlArgumentOperand(index));
	public BodyBuilder Ldloc(int index) => Add(ILOpCode.Ldloc, new IlLocalOperand(index));
	public BodyBuilder Br(int target) => Add(ILOpCode.Br, new IlBranchOperand(Anchor(target)));
	public BodyBuilder Brtrue(int target) => Add(ILOpCode.Brtrue, new IlBranchOperand(Anchor(target)));
	public BodyBuilder Leave(int target) => Add(ILOpCode.Leave, new IlBranchOperand(Anchor(target)));
	public BodyBuilder Switch(params int[] targets) => Add(ILOpCode.Switch, new IlSwitchOperand([.. targets.Select(Anchor)]));
	public BodyBuilder Call(IlMethodRef method) => Add(ILOpCode.Call, new IlMethodOperand(method));
	public BodyBuilder Newobj(IlMethodRef ctor) => Add(ILOpCode.Newobj, new IlMethodOperand(ctor));
	public BodyBuilder Calli(IlMethodSignature signature) => Add(ILOpCode.Calli, new IlCallSiteOperand(signature));
	public BodyBuilder ExceptionRegion(
		IlExceptionRegionKind kind,
		int tryStart,
		int tryEnd,
		int handlerStart,
		int handlerEnd,
		int? filterStart = null
	) {
		regions.Add(new IlExceptionRegion(
			kind,
			Anchor(tryStart),
			Anchor(tryEnd),
			Anchor(handlerStart),
			Anchor(handlerEnd),
			filterStart is int f ? Anchor(f) : null,
			kind == IlExceptionRegionKind.Catch ? IlTest.Exception : null
		));
		return this;
	}

	public IlMethodBody Build(
		IlMethodSignature? signature = null,
		ImmutableArray<IlTypeRef> locals = default,
		bool initLocals = true,
		IlLocalSignatureOrigin localsOrigin = default,
		InternalIlProvenance baselineProvenance = default
	) {
		if (!baselineProvenance.IsUnknown)
			for (int i = 0; i < instrs.Count; i++)
				instrs[i] = instrs[i] with { Provenance = baselineProvenance };
		List<IlAnchorId> anchors = new(instrs.Count + 1);
		for (int boundary = 0; boundary <= instrs.Count; boundary++)
			anchors.Add(Anchor(boundary));
		return IlMethodBody.CreateDecoded(
			IlTest.Method("Target", signature ?? IlTest.Sig(IlTest.Void)),
			initLocals,
			locals.IsDefault ? [] : locals,
			localsOrigin,
			instrs,
			anchors,
			regions
		);
	}
}

/// <summary>
/// A token resolver that hands out stable synthetic handles.
/// </summary>
/// <remarks>
/// Handles are assigned per kind from a counter and memoized by reference, so the same reference
/// always resolves to the same token and two different references never collide. Nothing here is
/// decodable; it exists to let encoder output be inspected byte by byte.
/// </remarks>
internal sealed class FakeTokenResolver : IIlTokenResolver {
	private readonly Dictionary<IlTypeRef, EntityHandle> types = new();
	private readonly Dictionary<IlMethodRef, EntityHandle> methods = new();
	private readonly Dictionary<IlFieldRef, EntityHandle> fields = new();
	private readonly Dictionary<IlMethodSignature, StandaloneSignatureHandle> callSites = new();
	private readonly Dictionary<string, UserStringHandle> strings = new(StringComparer.Ordinal);
	private int nextRow = 1;
	private int nextStringOffset = 1;

	/// <summary>
	/// Locals signature resolutions that arrived with a usable origin hint.
	/// </summary>
	public int LocalsOriginHits { get; private set; }

	/// <summary>
	/// Locals signature resolutions that arrived without one.
	/// </summary>
	public int LocalsOriginMisses { get; private set; }

	/// <summary>
	/// The handle <see cref="ResolveLocals"/> hands out when there is no usable origin.
	/// </summary>
	public StandaloneSignatureHandle SynthesizedLocals { get; } = MetadataTokens.StandaloneSignatureHandle(1);

	public EntityHandle ResolveType(IlTypeRef type) =>
		memoize(types, type, () => MetadataTokens.TypeReferenceHandle(nextRow++));

	public EntityHandle ResolveMethod(IlMethodRef method) =>
		memoize(methods, method, () => MetadataTokens.MemberReferenceHandle(nextRow++));

	public EntityHandle ResolveField(IlFieldRef field) =>
		memoize(fields, field, () => MetadataTokens.MemberReferenceHandle(nextRow++));

	public StandaloneSignatureHandle ResolveCallSite(IlMethodSignature signature) {
		if (!callSites.TryGetValue(signature, out StandaloneSignatureHandle handle)) {
			handle = MetadataTokens.StandaloneSignatureHandle(nextRow++);
			callSites[signature] = handle;
		}
		return handle;
	}

	public StandaloneSignatureHandle ResolveLocals(ImmutableArray<IlTypeRef> locals, IlLocalSignatureOrigin origin) {
		if (origin.IsValid) {
			LocalsOriginHits++;
			return origin.Handle;
		}
		LocalsOriginMisses++;
		return SynthesizedLocals;
	}

	public UserStringHandle ResolveUserString(string value) {
		if (!strings.TryGetValue(value, out UserStringHandle handle)) {
			handle = MetadataTokens.UserStringHandle(nextStringOffset);
			nextStringOffset += 2 * value.Length + 2;
			strings[value] = handle;
		}
		return handle;
	}

	private static EntityHandle memoize<T>(Dictionary<T, EntityHandle> map, T key, Func<EntityHandle> create)
		where T : notnull {
		if (!map.TryGetValue(key, out EntityHandle handle)) {
			handle = create();
			map[key] = handle;
		}
		return handle;
	}
}

/// <summary>
/// A resolver that fails on the nth resolution of any kind, for exercising partially-resolved
/// encode failures.
/// </summary>
internal sealed class FailingTokenResolver(int failAfter) : IIlTokenResolver {
	private readonly FakeTokenResolver inner = new();
	private int resolutions = 0;

	public EntityHandle ResolveType(IlTypeRef type) => chk(() => inner.ResolveType(type));
	public EntityHandle ResolveMethod(IlMethodRef method) => chk(() => inner.ResolveMethod(method));
	public EntityHandle ResolveField(IlFieldRef field) => chk(() => inner.ResolveField(field));

	public StandaloneSignatureHandle ResolveCallSite(IlMethodSignature signature) =>
		chk(() => inner.ResolveCallSite(signature));

	public StandaloneSignatureHandle ResolveLocals(ImmutableArray<IlTypeRef> locals, IlLocalSignatureOrigin origin) =>
		chk(() => inner.ResolveLocals(locals, origin));

	public UserStringHandle ResolveUserString(string value) => chk(() => inner.ResolveUserString(value));

	private T chk<T>(Func<T> resolve) => ++resolutions > failAfter
		? throw new InvalidOperationException("FailingTokenResolver counter exhausted")
		: resolve();
}
