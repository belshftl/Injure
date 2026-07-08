// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Hooks.Il;

namespace Injure.Mods.Runtime.Hooks;

internal enum HookDeclarationPhase {
	Load,
	Link,
}

internal enum RuntimeHookKind {
	Managed,
	Il,
}

internal readonly record struct RuntimeHookTargetKey(MethodBase Method, string? TargetId);

internal sealed class RuntimeHookOrder {
	public required string LocalId { get; init; }
	public int LocalPriority { get; init; } = 0;
	public IReadOnlyList<OwnerOrderingConstraint> Before { get; init; } = Array.Empty<OwnerOrderingConstraint>();
	public IReadOnlyList<OwnerOrderingConstraint> After { get; init; } = Array.Empty<OwnerOrderingConstraint>();
}

internal abstract class RuntimeHookDeclaration : IStrongRefDroppable {
	private RuntimeHookTargetKey? targetBacking;

	public required string OwnerId { get; init; }
	public required ReloadGeneration Generation { get; init; }
	public required HookDeclarationPhase Phase { get; init; }
	public required RuntimeHookKind Kind { get; init; }
	public required RuntimeHookTargetKey Target {
		get => targetBacking ?? throw new InternalStateException("hook target strong ref already been dropped");
		init => targetBacking = value;
	}
	public required RuntimeHookOrder Order { get; init; }

	public string HookId => $"{OwnerId}::{Order.LocalId}";

	public void DropStrongReferences() {
		targetBacking = null;
		OnDropStrongReferences();
	}
	public abstract void OnDropStrongReferences();
}

internal sealed class ManagedHookDeclaration : RuntimeHookDeclaration {
	private MethodInfo? hookMethod;

	public required MethodInfo HookMethod {
		get => hookMethod ?? throw new InternalStateException("managed hook method strong ref has already been dropped");
		init => hookMethod = value;
	}

	public override void OnDropStrongReferences() {
		hookMethod = null;
	}
}

internal sealed class IlHookDeclaration : RuntimeHookDeclaration {
	private IlManipulatorRegistration? registration;

	public required IlManipulatorRegistration Registration {
		get => registration ?? throw new InternalStateException("IL manipulator registration strong ref has already been dropped");
		init => registration = value;
	}

	public override void OnDropStrongReferences() {
		IlManipulatorRegistration? r = Interlocked.Exchange(ref registration, null);
		r?.DropStrongReferences();
	}
}
