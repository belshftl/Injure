// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.AnalyzerShared.Language;

// args:
//   - badCast: {0} = target variable type, {1} = type that the iterator yields
public abstract class ForeachAnalyzerBase(DiagnosticDescriptor badCast) : DiagnosticAnalyzer {
	public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(badCast);

	public sealed override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(analyzeForeach, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement);
	}

	private void analyzeForeach(SyntaxNodeAnalysisContext ctx) {
		var sx = (CommonForEachStatementSyntax)ctx.Node;
		ForEachStatementInfo info = ctx.SemanticModel.GetForEachStatementInfo(sx);
		Conversion conv = info.ElementConversion;
		if (!conv.Exists)
			return;
		if (conv.IsIdentity || conv.IsImplicit)
			return;

		ITypeSymbol? varType = null;
		if (sx is ForEachStatementSyntax simple)
			varType = ctx.SemanticModel.GetDeclaredSymbol(simple, ctx.CancellationToken)?.Type;

		Location? loc = sx switch {
			ForEachStatementSyntax s => s.Type.GetLocation(),
			ForEachVariableStatementSyntax vs => vs.Variable.GetLocation(),
			_ => sx.GetLocation(),
		};

		ctx.ReportDiagnostic(
			Diagnostic.Create(
				badCast,
				loc,
				varType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "<unknown>",
				info.ElementType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "<unknown>"
			)
		);
	}
}
