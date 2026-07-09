// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.Mods.Analyzers.BannedApis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MonoModNamespaceUsingAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.BannedApis.MonoModUsed
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				ctx.RegisterSyntaxNodeAction(analyzeUsingDirective, SyntaxKind.UsingDirective);
			}
		);
	}

	private static void analyzeUsingDirective(SyntaxNodeAnalysisContext ctx) {
		var sx = (UsingDirectiveSyntax)ctx.Node;
		if (sx.Alias is not null)
			return; // TODO
		ISymbol? sym = ctx.SemanticModel.GetSymbolInfo(sx.Name!, ctx.CancellationToken).Symbol;
		if (sym is not INamespaceSymbol ns)
			return;
		for (; ns.ContainingNamespace is { IsGlobalNamespace: false } parent; ns = parent);
		if (ns.Name is KnownMetadataNames.MonoModRootNamespace)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.BannedApis.MonoModUsed, sx.GetLocation()));
	}
}
