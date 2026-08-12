// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Usage;

internal static class TypeSymbolExtensions {
	extension([NotNullWhen(true)] ITypeSymbol? type) {
		public bool DerivesFrom(INamedTypeSymbol? candidateBase) {
			if (type is null || candidateBase is null)
				return false;
			for (ITypeSymbol? sym = type.BaseType; sym is not null; sym = sym.BaseType)
				if (SymbolEqualityComparer.Default.Equals(sym.OriginalDefinition, candidateBase.OriginalDefinition))
					return true;
			return false;
		}
	}
}
