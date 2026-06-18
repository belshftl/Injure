// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;

namespace Injure.ModKit.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModEntrypointAttribute : Attribute;

public interface IModEntrypoint<in TGameApi, L> where L : struct, IModLifetimeIdentity {
	ValueTask LoadAsync(IModLoadContext<TGameApi, L> ctx, BoundedCt<L> ct);
	ValueTask LinkAsync(IModLinkContext<TGameApi, L> ctx, BoundedCt<L> ct);
	ValueTask ActivateAsync(IModActivateContext<TGameApi, L> ctx, BoundedCt<L> ct);
	ValueTask DeactivateAsync(BoundedCt<L> ct);
	ValueTask UnloadAsync(BoundedCt<L> ct);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModReloadEntrypointAttribute : Attribute;

public interface IModReloadEntrypoint<in TGameApi, L> where L : struct, IModLifetimeIdentity {
	ValueTask<ModLiveStateBlob> SaveStateAsync(IModReloadContext<TGameApi, L> ctx, BoundedCt<L> ct);
	ValueTask RestoreStateAsync(IModReloadContext<TGameApi, L> ctx, ModLiveStateBlob state, BoundedCt<L> ct);
}
