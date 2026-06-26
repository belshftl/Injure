// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace Injure.Mods.Runtime;

internal static class ModLifetimeOwnerInference {
	public static string Infer<L>() where L : struct, IModLifetimeIdentity {
		ModLifetimeIdentityBelongsToAttribute attr = typeof(L).GetCustomAttribute<ModLifetimeIdentityBelongsToAttribute>() ??
			throw new InvalidOperationException($"lifetime identity type '{typeof(L)}' is missing a [ModLifetimeIdentityBelongsTo] attribute");
		return attr.OwnerID;
	}
}
