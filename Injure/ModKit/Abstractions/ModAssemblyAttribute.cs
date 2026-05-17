// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

// open enum since it must be usable in the attribute
public enum ModAssemblyHotReloadLevel {
	None = 1,
	SafeBoundary,
	Live,
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ModAssemblyAttribute(string ownerID, ModAssemblyHotReloadLevel hotReloadLevel) : Attribute {
	public string OwnerID { get; } = ownerID;
	public ModAssemblyHotReloadLevel HotReloadLevel { get; } = hotReloadLevel;
}
