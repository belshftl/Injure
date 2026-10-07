// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.StronglyTypedInt;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StronglyTypedIntAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget,
		Diagnostics.StronglyTypedInt.StronglyTypedIntMustBeReadonly,
		Diagnostics.StronglyTypedInt.StronglyTypedIntUnsupportedBacking,
		Diagnostics.StronglyTypedInt.StronglyTypedIntMemberCollision
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(analyzeNamedType, SymbolKind.NamedType);
	}

	private static void analyzeNamedType(SymbolAnalysisContext ctx) {
		var sym = (INamedTypeSymbol)ctx.Symbol;
		AttributeData? attr = Util.GetAttribute(sym, Constants.StronglyTypedInt.AttributeMetadataName);
		if (attr is not null)
			StronglyTypedIntModel.Validate(sym, attr, new DiagnosticSink(ctx.ReportDiagnostic), ctx.CancellationToken);
	}
}
