// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.CodeAnalysis;

namespace Injure.Mods.Abstractions.MethodModification;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric)]
public abstract class DetourAttribute : Attribute {
	private protected DetourAttribute() {
	}

	public string? LocalIdOverride { get; init; }
	public int LocalPriority { get; init; }

	public string[]? SoftBefore { get; init; }
	public string[]? SoftAfter { get; init; }

	public string[]? HardBefore { get; init; }
	public string[]? HardAfter { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric | MethodConstraints.ReturnsVoid)]
public abstract class PatchAttribute : Attribute {
	private protected PatchAttribute() {
	}

	public string? LocalIdOverride { get; init; }
	public int LocalPriority { get; init; }

	public string[]? SoftBefore { get; init; }
	public string[]? SoftAfter { get; init; }

	public string[]? HardBefore { get; init; }
	public string[]? HardAfter { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric)]
public sealed class LoadDetourAttribute(string targetId) : DetourAttribute {
	public string TargetId { get; } = targetId;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric | MethodConstraints.ReturnsVoid)]
public sealed class LoadPatchAttribute(string targetId) : PatchAttribute {
	public string TargetId { get; } = targetId;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric)]
public sealed class LoadMethodDetourAttribute(Type targetType, string methodName, BindingFlags bindingFlags) : DetourAttribute {
	public Type TargetType { get; } = targetType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric | MethodConstraints.ReturnsVoid)]
public sealed class LoadMethodPatchAttribute(Type targetType, string methodName, BindingFlags bindingFlags) : PatchAttribute {
	public Type TargetType { get; } = targetType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }
}
