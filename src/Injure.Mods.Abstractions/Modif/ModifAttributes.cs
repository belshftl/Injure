// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.CodeAnalysis;

namespace Injure.Mods.Abstractions.Modif;

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
public sealed class LoadDetourAttribute(Type generatedTargetType) : DetourAttribute {
	public Type GeneratedTargetType { get; } = generatedTargetType;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric | MethodConstraints.ReturnsVoid)]
public sealed class LoadPatchAttribute(Type generatedTargetType) : PatchAttribute {
	public Type GeneratedTargetType { get; } = generatedTargetType;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric)]
public sealed class LoadMethodDetourAttribute(Type declaringType, string methodName, BindingFlags bindingFlags) : DetourAttribute {
	public Type DeclaringType { get; } = declaringType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric | MethodConstraints.ReturnsVoid)]
public sealed class LoadMethodPatchAttribute(Type declaringType, string methodName, BindingFlags bindingFlags) : PatchAttribute {
	public Type DeclaringType { get; } = declaringType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }
}
