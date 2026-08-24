// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Injure.Mods.Abstractions.MethodModification.Il.Metadata;

/// <summary>
/// An optimization hint identifying the metadata standalone signature a body's locals were decoded from.
/// </summary>
/// <remarks>
/// <para>
/// A resolver may use this to skip re-encoding a locals signature when the hint names the module it
/// is emitting into and the locals list is unchanged. It is never part of equality and must never
/// change the resolved result: a resolver that ignores it entirely stays correct.
/// </para>
/// <para>
/// The <see langword="default"/> value carries no hint.
/// </para>
/// </remarks>
internal readonly record struct IlLocalSignatureOrigin(IlModuleIdentity Module, int RowId) {
	public bool IsValid => RowId > 0;
	public StandaloneSignatureHandle Handle => IsValid
		? MetadataTokens.StandaloneSignatureHandle(RowId)
		: throw new InternalStateException("local signature origin carries no handle");
}

/// <summary>
/// Resolves backend-neutral IL references into metadata handles valid for one output module.
/// </summary>
internal interface IIlTokenResolver {
	EntityHandle ResolveType(IlTypeRef type);
	EntityHandle ResolveMethod(IlMethodRef method);
	EntityHandle ResolveField(IlFieldRef field);
	StandaloneSignatureHandle ResolveCallSite(IlMethodSignature signature);
	StandaloneSignatureHandle ResolveLocals(ImmutableArray<IlTypeRef> locals, IlLocalSignatureOrigin origin);
	UserStringHandle ResolveUserString(string value);
}
