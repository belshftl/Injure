// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Runtime.MethodModification;

internal enum MethodModificationPhase {
	Load,
	Link,
}

internal readonly record struct RuntimeMethodTargetKey(MethodBase Method);

internal sealed class RuntimeModificationOrder {
	public required string LocalId { get; init; }
	public int LocalPriority { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint> Before { get; init; } = Array.Empty<OwnerOrderingConstraint>();
	public IReadOnlyList<OwnerOrderingConstraint> After { get; init; } = Array.Empty<OwnerOrderingConstraint>();
}

internal abstract class RuntimeMethodModificationDeclaration : IStrongRefDroppable {
	private RuntimeMethodTargetKey? targetBacking;

	public required string OwnerId { get; init; }
	public required ReloadGeneration Generation { get; init; }
	public required MethodModificationPhase Phase { get; init; }
	public required RuntimeMethodTargetKey Target {
		get => targetBacking ?? throw new InternalStateException("method modification target strong reference has already been dropped");
		init => targetBacking = value;
	}
	public string? TargetId { get; init; }
	public required RuntimeModificationOrder Order { get; init; }

	public string ModificationId => $"{OwnerId}::{Order.LocalId}";

	public void DropStrongReferences() {
		targetBacking = null;
		OnDropStrongReferences();
	}

	protected abstract void OnDropStrongReferences();
}

internal sealed class RuntimeDetourDeclaration : RuntimeMethodModificationDeclaration {
	private MethodInfo? detourMethod;

	public required MethodInfo DetourMethod {
		get => detourMethod ?? throw new InternalStateException("detour method strong reference has already been dropped");
		init => detourMethod = value;
	}

	protected override void OnDropStrongReferences() {
		detourMethod = null;
	}
}

internal sealed class RuntimePatchDeclaration : RuntimeMethodModificationDeclaration {
	private IlManipulatorRegistration? registration;

	public required IlManipulatorRegistration Registration {
		get => registration ?? throw new InternalStateException("IL manipulator registration strong reference has already been dropped");
		init => registration = value;
	}

	protected override void OnDropStrongReferences() {
		IlManipulatorRegistration? current = Interlocked.Exchange(ref registration, null);
		current?.DropStrongReferences();
	}
}
