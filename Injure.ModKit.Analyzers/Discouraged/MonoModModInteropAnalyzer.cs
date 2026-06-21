// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Discouraged;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MonoModModInteropAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Discouraged.MonoModModInteropUsed,
		Diagnostics.Discouraged.MonoModModInteropNamespaceUsed
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				KnownSymbols known = new(ctx.Compilation);
				ctx.RegisterOperationAction(c => analyzeInvocation(c, known), OperationKind.Invocation);
				ctx.RegisterSyntaxNodeAction(c => analyzeAttribute(c, known), SyntaxKind.Attribute);
				ctx.RegisterSyntaxNodeAction(c => analyzeUsingDirective(c, known), SyntaxKind.UsingDirective);
			}
		);
	}

	private static void analyzeInvocation(OperationAnalysisContext ctx, KnownSymbols known) {
		if (known.ModInteropManager is null)
			return;
		var inv = (IInvocationOperation)ctx.Operation;
		IMethodSymbol method = inv.TargetMethod.ReducedFrom ?? inv.TargetMethod;
		if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, known.ModInteropManager))
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.MonoModModInteropUsed, inv.Syntax.GetLocation()));
	}

	private static void analyzeAttribute(SyntaxNodeAnalysisContext ctx, KnownSymbols known) {
		var sx = (AttributeSyntax)ctx.Node;
		ISymbol? sym = ctx.SemanticModel.GetSymbolInfo(sx, ctx.CancellationToken).Symbol;
		INamedTypeSymbol? attrType = sym switch {
			IMethodSymbol ctor => ctor.ContainingType,
			INamedTypeSymbol type => type,
			_ => null,
		};
		if (
			!SymbolEqualityComparer.Default.Equals(attrType, known.ModExportNameAttribute) &&
			!SymbolEqualityComparer.Default.Equals(attrType, known.ModImportNameAttribute)
		)
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.MonoModModInteropUsed, sx.GetLocation()));
	}

	private static void analyzeUsingDirective(SyntaxNodeAnalysisContext ctx, KnownSymbols known) {
		var sx = (UsingDirectiveSyntax)ctx.Node;
		if (sx.Alias is not null)
			return; // TODO
		ISymbol? sym = ctx.SemanticModel.GetSymbolInfo(sx.Name!, ctx.CancellationToken).Symbol;
		if (sym is not INamespaceSymbol ns || !SymbolEqualityComparer.Default.Equals(ns, known.ModInterop))
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.MonoModModInteropNamespaceUsed, sx.GetLocation()));
	}
}
