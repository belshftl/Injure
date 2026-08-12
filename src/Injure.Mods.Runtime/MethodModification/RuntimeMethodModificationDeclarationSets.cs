// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions;

namespace Injure.Mods.Runtime.MethodModification;

internal abstract class RuntimeMethodModificationDeclarationSet<TDeclaration> : IStrongRefDroppable where TDeclaration : RuntimeMethodModificationDeclaration {
	private readonly string ownerId;
	private readonly string kindDisplay;
	private readonly HashSet<string> localIds = new(StringComparer.Ordinal);
	private List<TDeclaration>? declarations = new();

	protected RuntimeMethodModificationDeclarationSet(string ownerId, string kindDisplay) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		this.ownerId = ownerId;
		this.kindDisplay = kindDisplay;
	}

	public int Count {
		get {
			chk();
			return declarations.Count;
		}
	}

	public void Add(TDeclaration declaration) {
		InternalStateException.ThrowIfNull(declaration);
		chk();
		if (!StringComparer.Ordinal.Equals(declaration.OwnerId, ownerId))
			throw new InternalStateException($"{kindDisplay} declaration owner '{declaration.OwnerId}' does not match declaration set owner '{ownerId}'");
		if (!ModMetadataValidation.ValidateLocalId(declaration.Order.LocalId, out string? e))
			throw new MethodModificationValidationException($"invalid {kindDisplay} local id '{declaration.Order.LocalId}': {e}");
		if (!localIds.Add(declaration.Order.LocalId))
			throw new MethodModificationValidationException($"duplicate {kindDisplay} local id '{declaration.Order.LocalId}' for owner '{ownerId}'");
		declarations.Add(declaration);
	}

	public TDeclaration[] Snapshot() {
		chk();
		return declarations.Count == 0 ? Array.Empty<TDeclaration>() : declarations.ToArray();
	}

	public void DropStrongReferences() {
		if (declarations is null)
			return;
		foreach (TDeclaration d in declarations)
			d.DropStrongReferences();
		declarations.Clear();
		declarations = null;
		localIds.Clear();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(declarations))]
	private void chk() {
		if (declarations is null)
			throw new InternalStateException($"{kindDisplay} declaration set strong references have already been dropped");
	}
}

internal sealed class RuntimeDetourDeclarationSet(string ownerId) : RuntimeMethodModificationDeclarationSet<RuntimeDetourDeclaration>(ownerId, "detour");
internal sealed class RuntimePatchDeclarationSet(string ownerId) : RuntimeMethodModificationDeclarationSet<RuntimePatchDeclaration>(ownerId, "patch");
