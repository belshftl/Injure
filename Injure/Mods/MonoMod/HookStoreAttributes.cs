// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.MonoMod;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModHookTargetStoreAttribute(Type storeType) : Attribute {
	public Type StoreType { get; } = storeType;
}
