// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.Modif;

/// <summary>
/// Thrown if a mod's detour/patch declaration is malformed (unresolvable target, ambiguous between
/// multiple overloads, generated target's metadata token doesn't resolve, etc.)
/// </summary>
public sealed class ModifDeclarationException : Exception {
	internal ModifDeclarationException(string message) : base(message) {
	}

	internal ModifDeclarationException(string message, Exception inner) : base(message, inner) {
	}
}

/// <summary>
/// Resolves a mod's modif declarations into <see cref="MethodBase"/>s.
/// </summary>
internal static class ModifTargetResolver {
	public static MethodBase ResolveGeneratedTarget(Type generatedTargetType) {
		ArgumentNullException.ThrowIfNull(generatedTargetType);
		ModifTargetAttribute? marker = generatedTargetType.GetCustomAttribute<ModifTargetAttribute>() ??
			throw new ModifDeclarationException($"'{generatedTargetType}' is not marked with [ModifTarget]");
		try {
			return generatedTargetType.Module.ResolveMethod(marker.MetadataToken) ??
				throw new ModifDeclarationException(
					$"failed to resolve modif target metadata token 0x{marker.MetadataToken:x8} in module '{generatedTargetType.Module}'"
				);
		} catch (Exception ex) when (ex is ArgumentException or BadImageFormatException) {
			throw new ModifDeclarationException(
				$"failed to resolve modif target metadata token 0x{marker.MetadataToken:x8} in module '{generatedTargetType.Module}'", ex
			);
		}
	}

	public static MethodBase ResolveNamedTarget(Type declaringType, string methodName, BindingFlags bindingFlags, Type[]? parameterTypes) {
		ArgumentNullException.ThrowIfNull(declaringType);
		ArgumentNullException.ThrowIfNull(methodName);
		MethodBase? found = methodName == ConstructorInfo.ConstructorName || methodName == ConstructorInfo.TypeConstructorName
			? parameterTypes is null ? declaringType.GetConstructors(bindingFlags).SingleOrDefault() : declaringType.GetConstructor(bindingFlags, parameterTypes)
			: parameterTypes is null ? declaringType.GetMethod(methodName, bindingFlags) : declaringType.GetMethod(methodName, bindingFlags, parameterTypes);
		return found ?? throw new ModifDeclarationException($"could not find member '{methodName}' on '{declaringType}' with the given binding flags/parameter types");
	}
}
