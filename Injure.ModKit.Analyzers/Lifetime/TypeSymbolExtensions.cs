// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal static class TypeSymbolExtensions {
	extension([NotNullWhen(true)] ITypeSymbol? type) {
		public bool IsOrDerivesFrom(INamedTypeSymbol? candidateBase) {
			if (type is null || candidateBase is null)
				return false;
			for (ITypeSymbol? sym = type; sym is not null; sym = sym.BaseType)
				if (SymbolEqualityComparer.Default.Equals(sym.OriginalDefinition, candidateBase.OriginalDefinition))
					return true;
			return false;
		}

		public bool IsOrImplements(INamedTypeSymbol? iface) {
			if (type is null || iface is null)
				return false;
			if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, iface.OriginalDefinition))
				return true;
			return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iface.OriginalDefinition));
		}
	}
}
