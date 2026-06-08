// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;

using Injure.ModKit.Analyzers.Shared;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.ModKit.Analyzers.Discouraged;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LifecycleMemberAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Discouraged.LifecycleContextMember,
		Diagnostics.Discouraged.LifecycleContextLambdaCapture
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				if (!HotReloadModel.TryGetHotReloadLevel(ctx.Compilation, out ModAssemblyHotReloadLevelMirror lv) ||
					lv < ModAssemblyHotReloadLevelMirror.SafeBoundary)
					return;
				KnownSymbols known = new(ctx.Compilation);
				ctx.RegisterSymbolAction(c => analyzeField(c, known), SymbolKind.Field);
				ctx.RegisterSymbolAction(c => analyzeProperty(c, known), SymbolKind.Property);
				ctx.RegisterSyntaxNodeAction(
					c => analyzeLambda(c, known),
					SyntaxKind.SimpleLambdaExpression,
					SyntaxKind.ParenthesizedLambdaExpression,
					SyntaxKind.AnonymousMethodExpression
				);
			}
		);
	}

	private static void analyzeField(SymbolAnalysisContext ctx, KnownSymbols known) {
		var field = (IFieldSymbol)ctx.Symbol;
		if (field.IsImplicitlyDeclared)
			return;
		if (field.Type.IsLifecycleContext(known))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.LifecycleContextMember, field.Locations.FirstOrDefault()));
	}

	private static void analyzeProperty(SymbolAnalysisContext ctx, KnownSymbols known) {
		var property = (IPropertySymbol)ctx.Symbol;
		if (property.IsImplicitlyDeclared)
			return;
		if (property.Type.IsLifecycleContext(known))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.LifecycleContextMember, property.Locations.FirstOrDefault()));
	}

	private static void analyzeLambda(SyntaxNodeAnalysisContext ctx, KnownSymbols known) {
		var lambda = (AnonymousFunctionExpressionSyntax)ctx.Node;
		DataFlowAnalysis dataFlow = ctx.SemanticModel.AnalyzeDataFlow(lambda.Body);
		if (!dataFlow.Succeeded)
			return;
		foreach (ISymbol symbol in dataFlow.CapturedInside) {
			ITypeSymbol? type = symbol switch {
				ILocalSymbol l => l.Type,
				IParameterSymbol p => p.Type,
				IFieldSymbol f => f.Type,
				IPropertySymbol p => p.Type,
				_ => null,
			};
			if (type.IsLifecycleContext(known))
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.LifecycleContextLambdaCapture, lambda.GetLocation()));
		}
	}
}
