// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Sched.Tickers;

public interface ITickerRegistry {
	TickerHandle Add(in TickerSpec spec);
}
