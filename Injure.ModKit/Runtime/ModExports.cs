// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Runtime;

internal sealed class UntypedModExportTable : IStrongRefDroppable {
	private readonly Dictionary<Type, object> exports = new();
	private Type? lifetimeIdentityType;
	private int closed = 0;
	private bool dropped = false;
	private object? declsTyped;

	public UntypedModExportTable(Type lifetimeIdentityType) {
		this.lifetimeIdentityType = lifetimeIdentityType ?? throw new ArgumentNullException(nameof(lifetimeIdentityType));
		declsTyped = Activator.CreateInstance(typeof(ModExportDeclarationsImpl<>).MakeGenericType(lifetimeIdentityType), this) ??
			throw new InternalStateException("Activator.CreateInstance instantiation of ModExportDeclarationsImpl<> returned null");
	}

	public Type LifetimeIdentityType => lifetimeIdentityType ?? throw new InternalStateException("lifetime identity type strong ref has already been dropped");

	public void Add(Type contractType, object impl) {
		if (dropped)
			throw new InternalStateException("this UntypedModExportTable already dropped its strong references");

		ArgumentNullException.ThrowIfNull(contractType);
		ArgumentNullException.ThrowIfNull(impl);
		if (Volatile.Read(ref closed) != 0)
			throw new InvalidOperationException("the mod export table is closed");
		validateContractType(contractType);
		if (!contractType.IsInstanceOfType(impl))
			throw new ArgumentException($"implementation type '{impl.GetType()}' does not implement export contract '{contractType}'", nameof(impl));
		if (!exports.TryAdd(contractType, impl))
			throw new InvalidOperationException($"export contract '{contractType}' has already been registered");
	}

	public void Close() => Volatile.Write(ref closed, 1);

	public bool TryGet(Type contractType, [NotNullWhen(true)] out object? impl) {
		if (dropped)
			throw new InternalStateException("this UntypedModExportTable already dropped its strong references");

		ArgumentNullException.ThrowIfNull(contractType);
		validateContractType(contractType);
		return exports.TryGetValue(contractType, out impl);
	}

	public ModExportTableImpl<L, LDependency> AsTableView<L, LDependency>(ReloadGeneration generation) where L : struct, IModLifetimeIdentity where LDependency : struct, IModLifetimeIdentity {
		if (dropped)
			throw new InternalStateException("this UntypedModExportTable already dropped its strong references");
		if (typeof(LDependency) != lifetimeIdentityType)
			throw new InvalidOperationException($"this export table is bounded over {lifetimeIdentityType}, not {typeof(LDependency)}");
		return (ModExportTableImpl<L, LDependency>)(Activator.CreateInstance(typeof(ModExportTableImpl<,>).MakeGenericType(typeof(L), lifetimeIdentityType), this, generation) ??
			throw new InternalStateException("Activator.CreateInstance instantiation of ModExportTableImpl<,> returned null"));
	}

	public ModExportDeclarationsImpl<L> AsDeclsView<L>() where L : struct, IModLifetimeIdentity {
		if (dropped || declsTyped is null)
			throw new InternalStateException("this UntypedModExportTable already dropped its strong references");
		if (typeof(L) != lifetimeIdentityType)
			throw new InvalidOperationException($"this export table is bounded over {lifetimeIdentityType}, not {typeof(L)}");
		return (ModExportDeclarationsImpl<L>)declsTyped;
	}

	private void validateContractType(Type contractType) {
		if (!contractType.IsInterface)
			throw new ArgumentException($"export contract '{contractType}' is not an interface", nameof(contractType));
		Type expectedMarker = typeof(IModExportContract<>).MakeGenericType(LifetimeIdentityType);
		if (!expectedMarker.IsAssignableFrom(contractType))
			throw new ArgumentException($"export contract '{contractType}' is not bounded over lifetime identity '{lifetimeIdentityType}'", nameof(contractType));
	}

	public void DropStrongReferences() {
		dropped = true;
		declsTyped = null;
		Close();
		lifetimeIdentityType = null;
		exports.Clear();
	}
}

internal sealed class ModExportTableImpl<L, LDependency> : IModExportTable<L, LDependency>
	where L : struct, IModLifetimeIdentity
	where LDependency : struct, IModLifetimeIdentity
{
	private UntypedModExportTable? inner;
	private readonly ReloadGeneration generation;

	public ModExportTableImpl(UntypedModExportTable inner, ReloadGeneration generation) {
		ArgumentNullException.ThrowIfNull(inner);
		if (inner.LifetimeIdentityType != typeof(LDependency))
			throw new InternalStateException($"export table lifetime identity '{inner.LifetimeIdentityType}' does not match '{typeof(LDependency)}'");
		this.inner = inner;
		this.generation = generation;
	}

	public bool TryGet<TContract>([NotNullWhen(true)] out TContract? impl) where TContract : class, IModExportContract<LDependency> {
		if (inner is null)
			throw new ReloadGenerationExpiredException(generation);
		if (inner.TryGet(typeof(TContract), out object? v)) {
			impl = (TContract)v;
			return true;
		}
		impl = null;
		return false;
	}

	public TContract Require<TContract>() where TContract : class, IModExportContract<LDependency> {
		if (TryGet(out TContract? impl))
			return impl;
		throw new ModLoadException($"required export contract '{typeof(TContract)}' was not declared");
	}
}

internal sealed class ModExportDeclarationsImpl<L> : IModExportDeclarations<L> where L : struct, IModLifetimeIdentity {
	private readonly UntypedModExportTable inner;

	public ModExportDeclarationsImpl(UntypedModExportTable inner) {
		if (inner.LifetimeIdentityType != typeof(L))
			throw new InternalStateException("export declaration lifetime mismatch");
		this.inner = inner;
	}

	public void Add<TContract>(TContract impl) where TContract : class, IModExportContract<L> {
		ArgumentNullException.ThrowIfNull(impl);
		inner.Add(typeof(TContract), impl);
	}
}
