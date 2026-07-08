// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions;

namespace Injure.Mods.Runtime.Hooks;

internal sealed class RuntimeHookDeclarationSet : IStrongRefDroppable {
	private readonly string ownerId;
	private readonly HashSet<string> localIds = new(StringComparer.Ordinal);
	private List<RuntimeHookDeclaration>? decls = new();

	public RuntimeHookDeclarationSet(string ownerId) {
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		this.ownerId = ownerId;
	}

	public int Count {
		get {
			chk();
			return decls.Count;
		}
	}

	public void Add(RuntimeHookDeclaration declaration) {
		InternalStateException.ThrowIfNull(declaration);
		chk();
		if (!StringComparer.Ordinal.Equals(declaration.OwnerId, ownerId))
			throw new InternalStateException($"hook declaration owner '{declaration.OwnerId}' does not match declaration set owner '{ownerId}'");
		if (!ModMetadataValidation.ValidateLocalId(declaration.Order.LocalId, out string? e))
			throw new HookValidationException($"invalid hook local id '{declaration.Order.LocalId}': {e}");
		if (!localIds.Add(declaration.Order.LocalId))
			throw new HookValidationException($"duplicate hook local id '{declaration.Order.LocalId}' for owner '{ownerId}'");
		decls.Add(declaration);
	}

	public RuntimeHookDeclaration[] Snapshot() {
		chk();
		return decls.Count == 0 ? Array.Empty<RuntimeHookDeclaration>() : decls.ToArray();
	}

	public void DropStrongReferences() {
		if (decls is null)
			return;
		foreach (RuntimeHookDeclaration decl in decls)
			decl.DropStrongReferences();
		decls.Clear();
		decls = null;
		localIds.Clear();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(decls))]
	private void chk() {
		if (decls is null)
			throw new InternalStateException("hook declaration set strong refs have already been dropped");
	}
}
