// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace Injure.Mods.Weaver;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ModMethodTargetStoreAttribute(Type storeType) : Attribute {
	public Type StoreType { get; } = storeType;
}
