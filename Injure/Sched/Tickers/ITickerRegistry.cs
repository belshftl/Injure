// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.CodeAnalysis.Internal;

namespace Injure.Sched.Tickers;

[DontImplement]
public interface ITickerRegistry {
	TickerHandle Add(in TickerSpec spec);
}
