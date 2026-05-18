// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Injure.ModKit.Abstractions;

public interface IModReloadContext<out TGameApi> {
	TGameApi Api { get; }
	ReloadGeneration Generation { get; }
	IReadOnlySet<string> ReloadSet { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModReloadEntrypointAttribute : Attribute;

public interface IModReloadEntrypoint<in TGameApi> {
	ValueTask<ModLiveStateBlob> SaveStateAsync(IModReloadContext<TGameApi> ctx, CancellationToken ct);
	ValueTask RestoreStateAsync(IModReloadContext<TGameApi> ctx, ModLiveStateBlob state, CancellationToken ct);
}
