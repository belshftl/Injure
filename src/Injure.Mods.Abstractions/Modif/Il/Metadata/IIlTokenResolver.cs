// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il.Metadata;

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
