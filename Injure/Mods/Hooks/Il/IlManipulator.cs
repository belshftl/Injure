// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Hooks.Il;

public readonly struct IlContext<L> where L : struct, IModLifetimeIdentity {} // stub

public delegate void IlManipulator<L>(IlContext<L> ctx) where L : struct, IModLifetimeIdentity;
