// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Benchmarks.Modif.Il;

/// <summary>
/// Hands out fake meaningless tokens memoized per reference.
/// </summary>
/// <remarks>
/// Akin to the real resolver once the cache is warm, where it just does a lookup rather than
/// emitting a new metadata row.
/// </remarks>
internal sealed class MemoizingTokenResolver : IIlTokenResolver {
	private readonly Dictionary<IlTypeRef, EntityHandle> types = new();
	private readonly Dictionary<IlMethodRef, EntityHandle> methods = new();
	private readonly Dictionary<IlFieldRef, EntityHandle> fields = new();
	private readonly Dictionary<IlMethodSignature, StandaloneSignatureHandle> callsites = new();
	private readonly Dictionary<string, UserStringHandle> strings = new(StringComparer.Ordinal);
	private int nextRow = 1;
	private int nextStringOffset = 1;

	public EntityHandle ResolveType(IlTypeRef type) =>
		memoize(types, type, () => MetadataTokens.TypeReferenceHandle(nextRow++));

	public EntityHandle ResolveMethod(IlMethodRef method) =>
		memoize(methods, method, () => MetadataTokens.MemberReferenceHandle(nextRow++));

	public EntityHandle ResolveField(IlFieldRef field) =>
		memoize(fields, field, () => MetadataTokens.MemberReferenceHandle(nextRow++));

	public StandaloneSignatureHandle ResolveCallSite(IlMethodSignature signature) {
		if (!callsites.TryGetValue(signature, out StandaloneSignatureHandle handle)) {
			handle = MetadataTokens.StandaloneSignatureHandle(nextRow++);
			callsites[signature] = handle;
		}
		return handle;
	}

	public StandaloneSignatureHandle ResolveLocals(
		ImmutableArray<IlTypeRef> locals,
		IlLocalSignatureOrigin origin
	) => origin.IsValid ? origin.Handle : MetadataTokens.StandaloneSignatureHandle(1);

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
