// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace Injure.Mods.Abstractions.Hooks;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ModHookTargetStoreAttribute(Type storeType) : Attribute {
	public Type StoreType { get; } = storeType;
}
