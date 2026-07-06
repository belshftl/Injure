// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.Analyzers.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DontImplementAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
		Diagnostics.CodeAnalysis.DontImplementInterface
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				KnownTypes known = new(ctx.Compilation);
				if (known.DontImplementAttribute is null)
					return;
				ctx.RegisterSymbolStartAction(c => analyzeNamedType(c, known), SymbolKind.NamedType);
			}
		);
	}

	private static void analyzeNamedType(SymbolStartAnalysisContext ctx, KnownTypes known) {
		if (known.DontImplementAttribute is null)
			return;

		var type = (INamedTypeSymbol)ctx.Symbol;
		if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
			return;

		// use Interfaces instead of AllInterfaces, interfaces inherited through a base class
		// would be a false positive
		HashSet<INamedTypeSymbol> interfaces = new(SymbolEqualityComparer.Default);
		foreach (INamedTypeSymbol direct in type.Interfaces) {
			interfaces.Add(direct);
			foreach (INamedTypeSymbol inherited in direct.AllInterfaces)
				interfaces.Add(inherited);
		}

		interfaces.RemoveWhere(iface => !iface.OriginalDefinition.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.DontImplementAttribute)
			)
		);
		if (interfaces.Count == 0)
			return;

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
							!candidate.AllInterfaces.Any(inherited => SymbolEqualityComparer.Default.Equals(inherited, iface)
							)
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

					loc ??= type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
					if (loc is null)
						continue;
					c.ReportDiagnostic(
						Diagnostic.Create(
							Diagnostics.CodeAnalysis.DontImplementInterface,
							loc,
							iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
						)
					);
				}
			}
		);
	}
}
