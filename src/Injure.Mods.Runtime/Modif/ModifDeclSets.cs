// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Modif;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Detours;

namespace Injure.Mods.Runtime.Modif;

/// <summary>
/// One detour declaration. Not to be confused with <see cref="IDetourDecl{L}"/>.
/// </summary>
internal readonly record struct DetourDeclDesc(
	MethodBase Target,
	int LocalOrder,
	IReadOnlyList<OwnerOrderingConstraint>? Before,
	IReadOnlyList<OwnerOrderingConstraint>? After,
	DetourRegistration Registration
);

/// <summary>
/// One patch declaration. Not to be confused with <see cref="IPatchDecl{L}"/>.
/// </summary>
internal readonly record struct PatchDeclDesc(
	MethodBase Target,
	int LocalOrder,
	IReadOnlyList<OwnerOrderingConstraint>? Before,
	IReadOnlyList<OwnerOrderingConstraint>? After,
	IlManipulatorRegistration Registration
);

internal sealed class DetourDeclSet : IStrongRefDroppable {
	public string OwnerId { get; }

	public IReadOnlyList<DetourDeclDesc> Declarations {
		get {
			lock (@lock) {
				chk();
				return decls.ToImmutableArray();
			}
		}
	}

	private readonly Lock @lock = new();
	private List<DetourDeclDesc>? decls = new();

	public DetourDeclSet(string ownerId) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		OwnerId = ownerId;
	}

	public void Add(DetourDeclDesc decl) {
		lock (@lock) {
			chk();
			decls.Add(decl);
		}
	}

	public void DropStrongReferences() {
		if (decls is null)
			return;
		lock (@lock) {
			if (decls is null)
				return;
			decls.Clear();
			decls = null;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(decls))]
	private void chk() {
		if (decls is null)
			throw new InternalStateException("detour decl set has already been dropped");
	}
}

internal sealed class PatchDeclSet : IStrongRefDroppable {
	public string OwnerId { get; }

	public IReadOnlyList<PatchDeclDesc> Declarations {
		get {
			lock (@lock) {
				chk();
				return decls.ToImmutableArray();
			}
		}
	}

	private readonly Lock @lock = new();
	private List<PatchDeclDesc>? decls = new();

	public PatchDeclSet(string ownerId) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		OwnerId = ownerId;
	}

	public void Add(PatchDeclDesc decl) {
		lock (@lock) {
			chk();
			decls.Add(decl);
		}
	}

	public void DropStrongReferences() {
		if (decls is null)
			return;
		lock (@lock) {
			if (decls is null)
				return;
			decls.Clear();
			decls = null;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(decls))]
	private void chk() {
		if (decls is null)
			throw new InternalStateException("patch decl set has already been dropped");
	}
}
