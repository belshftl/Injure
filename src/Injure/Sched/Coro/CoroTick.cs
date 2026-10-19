// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Sched.Coro;

/// <remarks>
/// The <see langword="default"/> value is valid and is tick 0.
/// </remarks>
[StronglyTypedInt(typeof(long))]
public readonly partial struct CoroTick {
}
