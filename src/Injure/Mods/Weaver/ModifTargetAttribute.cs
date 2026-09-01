// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace Injure.Mods.Weaver;

[AttributeUsage(AttributeTargets.Delegate, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ModifTargetAttribute(int metadataToken) : Attribute {
	public int MetadataToken { get; } = metadataToken;
}
