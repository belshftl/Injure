// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Injure.ModKit.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModEntrypointAttribute : Attribute;

public interface IModEntrypoint<in TGameApi, TLifetime> where TLifetime : struct, IModLifetimeIdentity {
	ValueTask LoadAsync(IModLoadContext<TGameApi, TLifetime> ctx, CancellationToken ct);
	ValueTask LinkAsync(IModLinkContext<TGameApi, TLifetime> ctx, CancellationToken ct);
	ValueTask ActivateAsync(CancellationToken ct);
	ValueTask DeactivateAsync(CancellationToken ct);
	ValueTask UnloadAsync(CancellationToken ct);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModReloadEntrypointAttribute : Attribute;

public interface IModReloadEntrypoint<in TGameApi, TLifetime> where TLifetime : struct, IModLifetimeIdentity {
	ValueTask<ModLiveStateBlob> SaveStateAsync(IModReloadContext<TGameApi, TLifetime> ctx, CancellationToken ct);
	ValueTask RestoreStateAsync(IModReloadContext<TGameApi, TLifetime> ctx, ModLiveStateBlob state, CancellationToken ct);
}
