// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

using MonoMod.Cil;
using MonoMod.RuntimeDetour;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.MonoMod;

internal readonly record struct HookOrder(
	string OrderDomain,
	string LocalID,
	int LocalPriority
);

internal abstract class PatchDeclaration(string ownerID, HookOrder order, DetourConfig detourConfig) : IStrongRefDroppable {
	public string OwnerID { get; } = ownerID;
	public HookOrder Order { get; } = order;
	public DetourConfig DetourConfig { get; } = detourConfig;

	public abstract void Commit(IUntypedBoundedScope scope);
	public abstract void DropStrongReferences();
}

internal sealed class HookDeclaration(string ownerID, HookOrder order, DetourConfig detourConfig, MethodBase target, MethodInfo replacement)
	: PatchDeclaration(ownerID, order, detourConfig) {
	private MethodBase? target = target;
	private MethodInfo? replacement = replacement;

	public override void Commit(IUntypedBoundedScope scope) {
		ArgumentNullException.ThrowIfNull(scope);
		if (target is null || replacement is null)
			throw new InvalidOperationException("target/replacement method strong refs have already been dropped");
		Hook? h = null;
		try {
			h = new Hook(target, replacement, DetourConfig);
			scope.AddDisposable(new ClearableDisposable<Hook>(h));
			h = null;
		} finally {
			h?.Dispose();
		}
	}

	public override void DropStrongReferences() {
		target = null;
		replacement = null;
	}
}

internal sealed class ILHookDeclaration(string ownerID, HookOrder order, DetourConfig detourConfig, MethodBase target, MethodInfo manipulator)
	: PatchDeclaration(ownerID, order, detourConfig) {
	private MethodBase? target = target;
	private MethodInfo? manipulator = manipulator;

	public override void Commit(IUntypedBoundedScope scope) {
		ArgumentNullException.ThrowIfNull(scope);
		if (target is null || manipulator is null)
			throw new InvalidOperationException("target/manipulator method strong refs have already been dropped");
		ILContext.Manipulator? m;
		ILHook? h = null;
		try {
			m = (ILContext.Manipulator)Delegate.CreateDelegate(typeof(ILContext.Manipulator), manipulator);
			h = new ILHook(target, m, DetourConfig);
			scope.AddDisposable(new ClearableDisposable<ILHook>(h));
			h = null;
		} finally {
			h?.Dispose();
#pragma warning disable IDE0059 // unnecessary assignment to local
			m = null;
#pragma warning restore IDE0059 // unnecessary assignment to local
		}
	}

	public override void DropStrongReferences() {
		target = null;
		manipulator = null;
	}
}
