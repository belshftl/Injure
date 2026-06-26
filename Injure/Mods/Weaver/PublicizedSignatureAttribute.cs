// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.Mods.Weaver;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
public sealed class PublicizedSignatureAttribute : Attribute {
}
