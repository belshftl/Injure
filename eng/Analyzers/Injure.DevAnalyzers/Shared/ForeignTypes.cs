// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Shared;

// decides which assemblies' types count as foreign, i.e. must not appear in Injure's public API
// (IJDEV0100), which also decides the accessibility of generated mirror conversions
internal static class ForeignTypes {
	private static readonly ImmutableHashSet<string> whitelist = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"Injure",
		"Injure.Mods.Abstractions",
		"Injure.Mods.Runtime"
	);

	private static readonly ImmutableHashSet<string> bclPublicKeyTokens = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"b77a5c561934e089",
		"b03f5f7f11d50a3a",
		"7cec85d7bea7798e",
		"cc7b13ffcd2ddd51"
	);

	public static bool IsForeign(ITypeSymbol type, Compilation compilation) => !IsAllowedAssembly(
		type.ContainingAssembly,
		compilation.Assembly,
		compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly
	);

	public static bool IsAllowedAssembly(IAssemblySymbol? assembly, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		if (assembly is null)
			return true;
		if (SymbolEqualityComparer.Default.Equals(assembly, compilationAssembly))
			return true;
		if (whitelist.Contains(assembly.Identity.Name))
			return true;
		return isBclAssembly(assembly, coreLibrary);
	}

	private static bool isBclAssembly(IAssemblySymbol assembly, IAssemblySymbol coreLibrary) {
		if (SymbolEqualityComparer.Default.Equals(assembly, coreLibrary))
			return true;
		string name = assembly.Identity.Name;
		bool hasBclName =
			name == "mscorlib" ||
			name == "netstandard" ||
			name == "System" ||
			name.StartsWith("System.", StringComparison.Ordinal) ||
			name == "Microsoft.CSharp" ||
			name == "Microsoft.VisualBasic" ||
			name == "Microsoft.VisualBasic.Core" ||
			name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
		if (!hasBclName)
			return false;
		return bclPublicKeyTokens.Contains(publicKeyTokenString(assembly.Identity.PublicKeyToken));
	}

	private static string publicKeyTokenString(ImmutableArray<byte> token) {
		if (token.IsDefaultOrEmpty)
			return string.Empty;
		const string hex = "0123456789abcdef";
		char[] result = new char[token.Length * 2];
		for (int i = 0; i < token.Length; ++i) {
			result[i * 2] = hex[token[i] >> 4];
			result[i * 2 + 1] = hex[token[i] & 0xf];
		}
		return new string(result);
	}
}
