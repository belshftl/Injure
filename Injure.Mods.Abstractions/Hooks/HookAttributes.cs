// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.CodeAnalysis;

namespace Injure.Mods.Abstractions.Hooks;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric)]
public abstract class HookAttribute : Attribute {
	private protected HookAttribute() {
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
public sealed class LoadHookAttribute(string targetId) : HookAttribute {
	public string TargetId { get; } = targetId;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric | MethodConstraints.ReturnsVoid)]
public sealed class LoadIlHookAttribute(string targetId) : HookAttribute {
	public string TargetId { get; } = targetId;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric)]
public sealed class LoadMethodHookAttribute(Type targetType, string methodName, BindingFlags bindingFlags) : HookAttribute {
	public Type TargetType { get; } = targetType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MethodAttributeUsage(MethodConstraints.Static | MethodConstraints.NonGeneric | MethodConstraints.ReturnsVoid)]
public sealed class LoadMethodIlHookAttribute(Type targetType, string methodName, BindingFlags bindingFlags) : HookAttribute {
	public Type TargetType { get; } = targetType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }
}
