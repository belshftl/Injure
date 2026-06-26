// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;

namespace Injure.Mods;

/// <summary>
/// Marks the class that implements a code mod's entrypoint.
/// </summary>
/// <remarks>
/// The annotated class must implement <see cref="IModEntrypoint{TGameApi, L}"/> as a
/// closed generic and have a suitable parameterless constructor for use by the runtime.
/// The analyzer additionally enforces the target class to be <see langword="sealed"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModEntrypointAttribute : Attribute;

/// <summary>
/// Defines the entry point and "root" of a code mod.
/// </summary>
/// <typeparam name="TGameApi">
/// Game-specific API exposed to the mod.
/// </typeparam>
/// <typeparam name="L">
/// The mod's lifetime identity; see <c>Docs/mods/lifetime-identity.md</c> for more info.
/// </typeparam>
/// <remarks>
/// <para>
/// All methods on this interface are referred to as "mod lifecycle methods", or just
/// "lifecycle methods". Lifecycle methods are invoked by the runtime and must not be
/// invoked directly. The cancellation tokens passed to lifecycle methods are not
/// equivalent to <see cref="IBoundedScope{L}.Stopping"/>.
/// </para>
/// <para>
/// Unless otherwise noted, all lifecycle methods may be parallelized in a way that does
/// not disrupt dependency-relative ordering of calls (if applicable).
/// </para>
/// </remarks>
public interface IModEntrypoint<in TGameApi, L> where L : struct, IModLifetimeIdentity {
	/// <summary>
	/// Initializes this mod and state owned solely by the current generation.
	/// </summary>
	/// <param name="ctx">Load-time mod context.</param>
	/// <param name="ct">Cancellation token for this particular invocation.</param>
	/// <remarks>
	/// <para>
	/// Calls to <see cref="LoadAsync"/> across mods are not ordered according to the
	/// dependency graph. Other mods, including declared dependencies, must not be
	/// accessed during this callback.
	/// </para>
	/// <para>
	/// Load-hook declarations and export declarations close when this call completes;
	/// load hooks are applied, and the export declaration table is frozen and handed
	/// out to dependents during link.
	/// </para>
	/// </remarks>
	ValueTask LoadAsync(IModLoadContext<TGameApi, L> ctx, BoundedCt<L> ct);

	/// <summary>
	/// Integrates the current generation with its loaded declared dependencies.
	/// </summary>
	/// <param name="ctx">Link-time mod context.</param>
	/// <param name="ct">Cancellation token for this particular invocation.</param>
	/// <remarks>
	/// All enabled mods have completed <see cref="LoadAsync"/> before linking begins.
	/// Link calls are scheduled according to the resolved dependency graph.
	/// </remarks>
	ValueTask LinkAsync(IModLinkContext<TGameApi, L> ctx, BoundedCt<L> ct);

	/// <summary>
	/// Activates the current generation against the now-attached game.
	/// </summary>
	/// <param name="ctx">Activation-time mod context.</param>
	/// <param name="ct">Cancellation token for this particular invocation.</param>
	ValueTask ActivateAsync(IModActivateContext<TGameApi, L> ctx, BoundedCt<L> ct);

	/// <summary>
	/// Deactivates the current generation before the game is detached, the mod
	/// is reloaded or disabled, or the runtime shuts down.
	/// </summary>
	/// <param name="ct">Cancellation token for this particular invocation.</param>
	/// <remarks>
	/// For "create new state, then release it on deactivate" or other similar patterns, prefer
	/// custom <see cref="IReloadTeardown"/> (or <see cref="IDisposable"/> for more
	/// complex/state-owning/order-sensitive cases) objects registered into the activation scope,
	/// as it is less bug-prone and allows for better analyzer diagnostics.
	/// </remarks>
	ValueTask DeactivateAsync(BoundedCt<L> ct);

	/// <summary>
	/// Releases/undoes generation-owned state and performs other final cleanup before
	/// the generation scope is invalidated and its assembly load context is unloaded.
	/// </summary>
	/// <remarks>
	/// For "create new state, then release it on unload" or other similar patterns, prefer
	/// custom <see cref="IReloadTeardown"/> (or <see cref="IDisposable"/> for more
	/// complex/state-owning/order-sensitive cases) objects registered into the generation scope,
	/// as it is less bug-prone and allows for better analyzer diagnostics.
	/// </remarks>
	ValueTask UnloadAsync(BoundedCt<L> ct);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModReloadEntrypointAttribute : Attribute;

public interface IModReloadEntrypoint<in TGameApi, L> where L : struct, IModLifetimeIdentity {
	ValueTask<ModLiveStateBlob> SaveStateAsync(IModReloadContext<TGameApi, L> ctx, BoundedCt<L> ct);
	ValueTask RestoreStateAsync(IModReloadContext<TGameApi, L> ctx, ModLiveStateBlob state, BoundedCt<L> ct);
}
