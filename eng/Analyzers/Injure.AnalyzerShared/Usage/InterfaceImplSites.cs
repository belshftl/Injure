// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.AnalyzerShared.Usage;

/// <summary>
/// Shared by analyzers that report a type for implementing some interface, so that the diagnostic
/// goes on the base-list entry that brings the interface in, rather than on the type's name.
/// </summary>
internal static class InterfaceImplSites {
	/// <summary>
	/// The interfaces a type implements directly or through each directly implemented interface's
	/// direct interfaces. Leaves out interfaces inherited from a base class.
	/// </summary>
	public static HashSet<INamedTypeSymbol> Collect(INamedTypeSymbol type) {
		HashSet<INamedTypeSymbol> interfaces = new(SymbolEqualityComparer.Default);
		foreach (INamedTypeSymbol direct in type.Interfaces) {
			interfaces.Add(direct);
			foreach (INamedTypeSymbol inherited in direct.AllInterfaces)
				interfaces.Add(inherited);
		}
		return interfaces;
	}

	/// <summary>
	/// Reports once per interface at symbol end, at the earliest base-list entry that is or derives
	/// from that interface, or at the type name if no such entry is found.
	/// </summary>
	public static void Register(
		SymbolStartAnalysisContext ctx,
		INamedTypeSymbol type,
		IReadOnlyCollection<INamedTypeSymbol> interfaces,
		Action<SymbolAnalysisContext, INamedTypeSymbol, Location> report
	) {
		List<(INamedTypeSymbol Interface, Location Location)> locations = new();

		ctx.RegisterSyntaxNodeAction(
			c => {
				var baseList = (BaseListSyntax)c.Node;
				if (
					baseList.Parent is not BaseTypeDeclarationSyntax decl ||
					!SymbolEqualityComparer.Default.Equals(
						c.SemanticModel.GetDeclaredSymbol(decl, c.CancellationToken),
						type
					)
				)
					return;

				foreach (BaseTypeSyntax baseTypeSyntax in baseList.Types) {
					var candidate = c.SemanticModel.GetTypeInfo(baseTypeSyntax.Type, c.CancellationToken).Type as INamedTypeSymbol;
					if (candidate?.TypeKind != TypeKind.Interface)
						continue;

					foreach (INamedTypeSymbol iface in interfaces) {
						if (
							!SymbolEqualityComparer.Default.Equals(candidate, iface) &&
							!candidate.AllInterfaces.Any(inherited => SymbolEqualityComparer.Default.Equals(inherited, iface))
						)
							continue;

						lock (locations)
							locations.Add((iface, baseTypeSyntax.GetLocation()));
					}
				}
			},
			SyntaxKind.BaseList
		);

		ctx.RegisterSymbolEndAction(c => {
				(INamedTypeSymbol Interface, Location Location)[] found;
				lock (locations)
					found = locations.ToArray();

				foreach (INamedTypeSymbol iface in interfaces) {
					Location? loc = null;
					foreach (SyntaxReference sx in type.DeclaringSyntaxReferences) {
						foreach ((INamedTypeSymbol candidate, Location candidateLoc) in found) {
							if (
								!SymbolEqualityComparer.Default.Equals(candidate, iface) ||
								candidateLoc.SourceTree != sx.SyntaxTree ||
								!sx.Span.Contains(candidateLoc.SourceSpan)
							)
								continue;

							if (loc is null || candidateLoc.SourceSpan.Start < loc.SourceSpan.Start)
								loc = candidateLoc;
						}

						if (loc is not null)
							break;
					}

					loc ??= type.Locations.FirstOrDefault(static l => l.IsInSource);
					if (loc is not null)
						report(c, iface, loc);
				}
			}
		);
	}
}
