// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

// open enum since it must be usable in the attribute
// NOTE: this enum's numeric values are ABI, do NOT change them
// this enum's numeric values are also mirrored in Injure.ModKit.Analyzers/Core/ModAssemblyAttributeReader.cs
public enum ModAssemblyHotReloadLevel {
	None = 1,
	SafeBoundary = 2,
	Live = 3,
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ModAssemblyAttribute(string ownerID, ModAssemblyHotReloadLevel hotReloadLevel) : Attribute {
	public string OwnerID { get; } = ownerID;
	public ModAssemblyHotReloadLevel HotReloadLevel { get; } = hotReloadLevel;
}
