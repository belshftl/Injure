// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.Analyzers.Language;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticEventAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Language.StaticEventDeclared
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				ctx.RegisterSymbolAction(analyzeEvent, SymbolKind.Event);
			}
		);
	}

	private static void analyzeEvent(SymbolAnalysisContext ctx) {
		if (!ctx.Symbol.IsStatic)
			return;
		Location? loc = ctx.Symbol.Locations.FirstOrDefault(static l => l.IsInSource);
		if (loc is null || loc == Location.None)
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Language.StaticEventDeclared, loc));
	}
}
