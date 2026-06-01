// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Coroutines;
using Injure.Input;
using Injure.ModKit.Abstractions;
using Injure.Timing;

namespace Injure.Layers;

internal sealed class LayerRuntime : ILayerTickFeeder, IDisposable {
	public LayerTimeDomain Time { get; }
	public CoroutineScheduler Coroutines { get; }
	public CoroutineScope CoroutineScope { get; }

	private readonly List<IMonoTickReceiver> toUpdate;
	private ActionContext? actionCtx;

	public LayerRuntime() {
		Time = new LayerTimeDomain();
		Coroutines = new CoroutineScheduler();
		CoroutineScope = CoroutineScope.CreateRoot(Coroutines, "Layer", EngineInfo.OwnerID); // TODO think about what owner ID this should use
		toUpdate = new List<IMonoTickReceiver>();
	}

	public T Feed<T>(T obj) where T : class, IMonoTickReceiver {
		ArgumentNullException.ThrowIfNull(obj);
		toUpdate.Add(obj);
		return obj;
	}

	public void InitActions(ActionProfile? profile) {
		actionCtx = profile is null ? null : new ActionContext(profile);
	}

	public void UpdateTickFed(MonoTick tick) {
		foreach (IMonoTickReceiver r in toUpdate)
			r.Update(tick);
	}

	public ControlView UpdateControls(MonoTick tick, in InputView input) {
		if (actionCtx is null)
			return new ControlView(ActionStateView.Empty, ReadOnlySpan<ControlEvent>.Empty, input.State.Pointer);
		return actionCtx.Update(tick, input);
	}

	public void SuppressControls(MonoTick tick) {
		if (actionCtx is null)
			return;
		_ = actionCtx.Update(tick, InputView.Empty);
	}

	public void TickCoroutines(double dt, double rawDt) {
		Coroutines.Tick(dt, rawDt, CoroUpdatePhase.Update);
	}

	public void Dispose() {
		CoroutineScope.Cancel();
		Coroutines.Dispose();
	}
}
