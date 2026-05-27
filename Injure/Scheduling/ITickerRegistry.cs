// SPDX-License-Identifier: MIT

namespace Injure.Scheduling;

public interface ITickerRegistry {
	TickerHandle Add(in TickerSpec spec);
}
