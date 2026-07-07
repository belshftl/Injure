// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Hooks.Il;

/// <summary>
/// Uses the passed <see cref="IlEmitter"/> to emit a transaction-local instruction fragment.
/// </summary>
/// <param name="emitter">
/// Emitter given to this callback.
/// </param>
/// <remarks>
/// If the callback throws, the entire fragment is discarded.
/// </remarks>
public delegate void IlEmitAction(IlEmitter emitter);
