// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Sched.Coro;

public readonly struct CoroContext {
	public CoroScheduler Scheduler { get; }
	public CoroHandle Handle { get; }
	public CoroScope Scope { get; }
	public double DeltaTime { get; }
	public double RawDeltaTime { get; }
	public CoroUpdatePhase Phase { get; }
	public CoroTick Tick { get; }

	internal CoroContext(
		CoroScheduler sched,
		CoroHandle handle,
		CoroScope scope,
		double dt,
		double rawDt,
		CoroUpdatePhase phase,
		CoroTick tick
	) {
		Scheduler = sched;
		Handle = handle;
		Scope = scope;
		DeltaTime = dt;
		RawDeltaTime = rawDt;
		Phase = phase;
		Tick = tick;
	}
}
