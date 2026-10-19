// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.AnalyzerShared.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.Analyzers.Usage;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DontImplementAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
		Diagnostics.Usage.DontImplementInterface
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

		HashSet<INamedTypeSymbol> interfaces = InterfaceImplSites.Collect(type);
		interfaces.RemoveWhere(iface => !iface.OriginalDefinition.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.DontImplementAttribute)));
		if (interfaces.Count == 0)
			return;

		InterfaceImplSites.Register(ctx, type, interfaces, static (c, iface, loc) =>
			c.ReportDiagnostic(Diagnostic.Create(
				Diagnostics.Usage.DontImplementInterface,
				loc,
				iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
			))
		);
	}
}
