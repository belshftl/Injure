// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
public sealed class DoesNotCreateObligationAttribute : Attribute;
