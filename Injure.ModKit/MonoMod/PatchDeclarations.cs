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

internal abstract class PatchDeclaration(string ownerID, HookOrder order) : IStrongRefDroppable {
	public string OwnerID { get; } = ownerID;
	public HookOrder Order { get; } = order;

	public abstract void Commit(IActiveOwnerScope scope);
	public abstract void DropStrongReferences();
}

internal sealed class HookDeclaration(string ownerID, HookOrder order, MethodBase target, MethodInfo replacement) : PatchDeclaration(ownerID, order) {
	private MethodBase? target = target;
	private MethodInfo? replacement = replacement;

	public override void Commit(IActiveOwnerScope scope) {
		ArgumentNullException.ThrowIfNull(scope);
		if (target is null || replacement is null)
			throw new InvalidOperationException("target/replacement method strong refs have already been dropped");
		Hook? h = null;
		try {
			h = new Hook(target, replacement);
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

internal sealed class ILHookDeclaration(string ownerID, HookOrder order, MethodBase target, MethodInfo manipulator) : PatchDeclaration(ownerID, order) {
	private MethodBase? target = target;
	private MethodInfo? manipulator = manipulator;

	public override void Commit(IActiveOwnerScope scope) {
		ArgumentNullException.ThrowIfNull(scope);
		if (target is null || manipulator is null)
			throw new InvalidOperationException("target/manipulator method strong refs have already been dropped");
		ILContext.Manipulator? m;
		ILHook? h = null;
		try {
			m = (ILContext.Manipulator)Delegate.CreateDelegate(typeof(ILContext.Manipulator), manipulator);
			h = new ILHook(target, m);
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
