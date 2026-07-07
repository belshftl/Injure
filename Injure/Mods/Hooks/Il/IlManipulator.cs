// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Hooks.Il;

/// <summary>
/// Manipulates one snapshot of a method body as part of an ordered IL transformation pipeline.
/// </summary>
/// <typeparam name="L">
/// Lifetime identity of the owner; see <c>Docs/mods/lifetime-identity.md</c> for more info.
/// </typeparam>
/// <param name="ctx">
/// Transaction-scoped manipulation context.
/// </param>
/// <remarks>
/// Matching observes the method body as it existed when this manipulator started. Edits that are
/// declared by this callback are committed atomically and become visible to later manipulators
/// when the callback returns successfully; if it throws, the edits are discarded.
/// </remarks>
public delegate void IlManipulator<L>(IlContext<L> ctx) where L : struct, IModLifetimeIdentity;
