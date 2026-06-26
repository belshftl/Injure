// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace Injure.Mods.MonoMod;

public interface IHookAttribute {
	string? OrderDomain { get; }
	int LocalPriority { get; }

	string? DetourIDOverride { get; }
	string[]? DetourBefore { get; }
	string[]? DetourAfter { get; }
	int? DetourPriority { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class LoadHookAttribute(string targetID) : Attribute, IHookAttribute {
	public string TargetID { get; } = targetID;

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }

	public string? DetourIDOverride { get; init; }
	public string[]? DetourBefore { get; init; }
	public string[]? DetourAfter { get; init; }
	public int? DetourPriority { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class LoadILHookAttribute(string targetID) : Attribute, IHookAttribute {
	public string TargetID { get; } = targetID;

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }

	public string? DetourIDOverride { get; init; }
	public string[]? DetourBefore { get; init; }
	public string[]? DetourAfter { get; init; }
	public int? DetourPriority { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class LoadMethodHookAttribute(Type targetType, string methodName, BindingFlags bindingFlags) : Attribute, IHookAttribute {
	public Type TargetType { get; } = targetType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }

	public string? DetourIDOverride { get; init; }
	public string[]? DetourBefore { get; init; }
	public string[]? DetourAfter { get; init; }
	public int? DetourPriority { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class LoadMethodILHookAttribute(Type targetType, string methodName, BindingFlags bindingFlags) : Attribute, IHookAttribute {
	public Type TargetType { get; } = targetType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }

	public string? DetourIDOverride { get; init; }
	public string[]? DetourBefore { get; init; }
	public string[]? DetourAfter { get; init; }
	public int? DetourPriority { get; init; }
}
