// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Internals.Analyzers.PublicApi;

internal static class SymbolExtensions {
	extension(ISymbol sym) {
		public bool IsPubliclyVisible() {
			for (ISymbol? curr = sym; curr is not null; curr = curr.ContainingType)
				switch (curr.DeclaredAccessibility) {
				case Accessibility.Public:
				case Accessibility.Protected:
				case Accessibility.ProtectedOrInternal:
					break;
				default:
					return false;
				}
			return true;
		}
	}
}
