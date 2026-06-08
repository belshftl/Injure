// SPDX-License-Identifier: MIT

using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Core;

internal static class SymbolHelpers {
	public static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeType, out AttributeData attribute) {
		if (attributeType is not null)
			foreach (AttributeData attr in symbol.GetAttributes())
				if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeType)) {
					attribute = attr;
					return true;
				}
		attribute = null!;
		return false;
	}

	public static Location GetBestLocation(ISymbol symbol, AttributeData? attribute = null) {
		SyntaxReference? sx = attribute?.ApplicationSyntaxReference;
		if (sx is not null)
			return sx.GetSyntax().GetLocation();
		return symbol.Locations.FirstOrDefault() ?? Location.None;
	}

	public static string GetFullMetadataName(INamedTypeSymbol type) {
		StringBuilder sb = new();
		appendContainingNs(sb, type.ContainingNamespace);
		appendType(sb, type);
		return sb.ToString();
	}

	private static void appendContainingNs(StringBuilder sb, INamespaceSymbol? ns) {
		if (ns is null || ns.IsGlobalNamespace)
			return;
		appendContainingNs(sb, ns.ContainingNamespace);
		if (sb.Length != 0)
			sb.Append('.');
		sb.Append(ns.MetadataName);
	}

	private static void appendType(StringBuilder sb, INamedTypeSymbol type) {
		if (type.ContainingType is not null) {
			appendType(sb, type.ContainingType);
			sb.Append('+');
		} else if (sb.Length != 0) {
			sb.Append('.');
		}
		sb.Append(type.MetadataName);
	}

	/*
	public static bool IsSelfGenerated(INamedTypeSymbol type, Compilation comp) {
		INamedTypeSymbol? generatedAttr = comp.GetTypeByMetadataName(KnownTypeMetadataNames.GeneratedCodeAttribute);
		if (generatedAttr is not null && type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, generatedAttr)))
			return true;
		foreach (SyntaxReference sx in type.DeclaringSyntaxReferences) {
			string path = sx.SyntaxTree.FilePath;
			if (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}
	*/
}
