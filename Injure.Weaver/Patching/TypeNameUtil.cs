// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Text;

using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class TypeNameUtil {
	public static (string Namespace, string Name) SplitFullTypeName(string fullName) {
		int index = fullName.LastIndexOf('.');
		if (index < 0)
			return ("", fullName);
		return (fullName[..index], fullName[(index + 1)..]);
	}

	public static string SanitizeIdentifier(string value) {
		if (string.IsNullOrWhiteSpace(value))
			return "_";

		StringBuilder sb = new(value.Length + 1);

		char first = value[0];
		if (isIdentStart(first))
			sb.Append(first);
		else
			sb.Append('_');

		for (int i = 1; i < value.Length; i++) {
			char ch = value[i];
			sb.Append(isIdentPart(ch) ? ch : '_');
		}

		return sb.ToString();
	}

	public static string GetContainerName(TypeDefinition declaringType) {
		List<string> parts = new();
		TypeDefinition? current = declaringType;
		while (current is not null) {
			parts.Add(SanitizeIdentifier(trimGenericArity(current.Name)));
			current = current.DeclaringType;
		}
		return string.Join("_", parts);
	}

	public static string GetMethodBaseName(MethodDefinition method) {
		if (method.IsConstructor)
			return method.IsStatic ? "cctor" : "ctor";
		return SanitizeIdentifier(trimGenericArity(method.Name));
	}

	public static string GetDisplayName(MethodDefinition method) => method.FullName;

	private static string trimGenericArity(string name) {
		int tick = name.IndexOf('`');
		return tick >= 0 ? name[..tick] : name;
	}

	private static bool isIdentStart(char ch) => ch == '_' || char.IsLetter(ch);
	private static bool isIdentPart(char ch) => ch == '_' || char.IsLetterOrDigit(ch);
}
