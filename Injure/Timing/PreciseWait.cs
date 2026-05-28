// SPDX-License-Identifier: MIT

namespace Injure.Timing;

public static partial class PreciseWait {
	static PreciseWait() {
		global::Injure.Native.PreciseWait.Init();
	}

	public static void Wait(long ns) => global::Injure.Native.PreciseWait.Wait(ns);
}
