// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

using Injure.Coroutines;
using Injure.Graphics;
using Injure.Input;
using Injure.Internals.Analyzers.Attributes;
using Injure.Scheduling;

namespace Injure.Layers;

[ClosedFlags]
public readonly partial struct LayerFeatures {
	[Flags]
	public enum Bits {
		None = 0,
		Render = 1 << 0,
		Input = 1 << 1,
	}
}

[ClosedFlags]
public readonly partial struct LayerBlockMask {
	[Flags]
	public enum Bits {
		None = 0,
		Update = 1 << 0,
		Render = 1 << 1,
		Input = 1 << 2,
	}
}

public readonly record struct LayerBlockRule(LayerBlockMask Blocked, LayerTagSet MatchTags);

/// <summary>
/// Base type for a layer managed by a <see cref="LayerStack"/>.
/// </summary>
/// <remarks>
/// <para>
/// A layer has a mandatory <see cref="Update(in LayerTickContext, in TickDeadline)"/> driven
/// by the ticker it was pushed with and may optionally render or receive input.
/// </para>
/// <para>
/// Expensive preparation should be done in <see cref="WarmAsync"/>. Activation-bound
/// services such as time domain, coroutines, <see cref="Timing.IMonoTickReceiver"/> auto-update,
/// etc. are only available from <see cref="OnEnter"/> until <see cref="OnLeave"/> returns.
/// </para>
/// </remarks>
public abstract class Layer {
	internal LayerStack? Owner { get; set; }
	internal LayerRuntime? Runtime { get; private set; }

	private const string eMsg =
		"activation-bound layer services (time domain, coroutines, IMonoTickReceiver auto-update) are not available yet; most likely, you have to move this code from the constructor or WarmAsync to OnEnter";

	/// <summary>
	/// Runtime-provided time domain for this layer.
	/// </summary>
	/// <remarks>
	/// This is an activation-bound service.
	/// </remarks>
	protected LayerTimeDomain Time => Runtime?.Time ?? throw new InvalidOperationException(eMsg);

	/// <summary>
	/// Runtime-provided coroutine scheduler for this layer.
	/// </summary>
	/// <remarks>
	/// This is an activation-bound service.
	/// </remarks>
	protected CoroutineScheduler Coroutines => Runtime?.Coroutines ?? throw new InvalidOperationException(eMsg);

	/// <summary>
	/// Runtime-provided coroutine scope for this layer, intended to be paired with <see cref="Coroutines"/>.
	/// </summary>
	/// <remarks>
	/// This is an activation-bound service.
	/// </remarks>
	protected CoroutineScope CoroutineScope => Runtime?.CoroutineScope ?? throw new InvalidOperationException(eMsg);

	/// <summary>
	/// Runtime-provided automatic updater of <see cref="Timing.IMonoTickReceiver"/>
	/// objects for this layer.
	/// </summary>
	/// <remarks>
	/// This is an activation-bound service.
	/// </remarks>
	protected ILayerTickFeeder TickFeeder => Runtime ?? throw new InvalidOperationException(eMsg);

	internal void AttachRuntime(LayerRuntime runtime) {
		Runtime = runtime ?? throw new InternalStateException("AttachRuntime got passed null");
	}

	internal void DetachRuntime() {
		if (Runtime is null)
			throw new InternalStateException("no layer runtime is attached");
		Runtime.Dispose();
		Runtime = null;
	}

	/// <summary>
	/// The optional features this layer participates in.
	/// </summary>
	public virtual LayerFeatures Features => LayerFeatures.Render;

	/// <summary>
	/// Tags used by other layers' block rules to match this layer.
	/// </summary>
	/// <remarks>
	/// This value may be snapshotted/cached by the layer stack on activation; changes
	/// to the returned value may not take effect until the layer is re-entered. A refresh
	/// API is planned.
	/// </remarks>
	public virtual LayerTagSet Tags => LayerTagSet.Empty;

	/// <summary>
	/// Block rules this layer applies to layers below it in the stack.
	/// </summary>
	public virtual ReadOnlySpan<LayerBlockRule> BlockRules => ReadOnlySpan<LayerBlockRule>.Empty;

	/// <summary>
	/// Action profile used by the layer's primary action context, or <see langword="null"/>
	/// if the layer doesn't use the primary action context.
	/// </summary>
	public virtual ActionProfile? ActionProfile => null;

	/// <summary>
	/// Performs preparation work before the layer is activated. May run on another thread.
	/// </summary>
	/// <param name="ct">Cancellation token, fired if the layer's activation is cancelled.</param>
	/// <returns>
	/// A task representing the warmup operation.
	/// </returns>
	/// <remarks>
	/// <para>
	/// This method may run on a background thread; all preparation work must be thread-safe and
	/// not rely on main-thread-only APIs. Activation-bound services should also not be touched.
	/// </para>
	/// <para>
	/// <see cref="OnEnter()"/> is called later on the host thread if this task completes successfully.
	/// </para>
	/// </remarks>
	public abstract Task WarmAsync(CancellationToken ct);

	/// <summary>
	/// Called after warmup completes and the layer becomes active.
	/// </summary>
	public abstract void OnEnter();

	/// <summary>
	/// Called when this layer's ticker fires and update is not blocked by a higher layer.
	/// </summary>
	/// <param name="ctx">Tick context containing timing/input.</param>
	/// <param name="deadline">Target tick deadline, as provided by the ticker callback.</param>
	public abstract void Update(in LayerTickContext ctx, in TickDeadline deadline);

	/// <summary>
	/// Called during rendering if this layer has the render feature and render is not blocked by a higher layer.
	/// </summary>
	/// <param name="cv">Canvas for this render frame.</param>
	public abstract void Render(Canvas cv);

	/// <summary>
	/// Called before the layer is deactivated.
	/// </summary>
	/// <remarks>
	/// Activation-bound services are still available in this method; they're detached immediately
	/// after it returns.
	/// </remarks>
	public abstract void OnLeave();
}
