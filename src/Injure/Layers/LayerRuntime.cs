// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Input;
using Injure.Mods;
using Injure.Sched.Coro;
using Injure.Host;
using CoroUpdatePhase = Injure.Sched.Coro.CoroUpdatePhase;

namespace Injure.Layers;

internal sealed class LayerRuntime : ILayerTickFeeder, IDisposable {
	public LayerTimeDomain Time { get; }
	public CoroScheduler Coroutines { get; }
	public CoroScope CoroutineScope { get; }

	private readonly List<IHostTickReceiver> toUpdate;
	private ActionCtx? actionCtx;

	public LayerRuntime() {
		Time = new LayerTimeDomain();
		Coroutines = new CoroScheduler();
		CoroutineScope = CoroScope.CreateRoot(Coroutines, "Layer", EngineInfo.OwnerId); // TODO think about what owner ID this should use
		toUpdate = new List<IHostTickReceiver>();
	}

	public T Feed<T>(T obj) where T : class, IHostTickReceiver {
		ArgumentNullException.ThrowIfNull(obj);
		toUpdate.Add(obj);
		return obj;
	}

	public void InitActions(ActionProfile? profile) {
		actionCtx = profile is null ? null : new ActionCtx(profile);
	}

	public void UpdateTickFed(HostTick tick) {
		foreach (IHostTickReceiver r in toUpdate)
			r.Update(tick);
	}

	public ControlView UpdateControls(HostTick tick, in InputView input) {
		if (actionCtx is null)
			return new ControlView(ActionStateView.Empty, ReadOnlySpan<ControlEvent>.Empty, input.State.Pointer);
		return actionCtx.Update(tick, input);
	}

	public void SuppressControls(HostTick tick) {
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
