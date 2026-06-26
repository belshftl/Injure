// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.Mods.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
public sealed class DoesNotCreateObligationAttribute : Attribute;
