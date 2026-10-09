// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.ColorType;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ColorTypeAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.ColorType.ColorTypeInvalidTarget,
		Diagnostics.ColorType.ColorTypeMustBeReadonly,
		Diagnostics.ColorType.ColorTypeMemberCollision
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(analyzeNamedType, SymbolKind.NamedType);
	}

	private static void analyzeNamedType(SymbolAnalysisContext ctx) {
		var sym = (INamedTypeSymbol)ctx.Symbol;
		DiagnosticSink sink = new(ctx.ReportDiagnostic);
		if (Util.GetAttribute(sym, Constants.ColorType.Color32AttributeMetadataName) is AttributeData a32)
			ColorTypeModel.Validate(sym, a32, ColorTypeKind.Color32, sink, ctx.CancellationToken);
		else if (Util.GetAttribute(sym, Constants.ColorType.ColorF128AttributeMetadataName) is AttributeData af128)
			ColorTypeModel.Validate(sym, af128, ColorTypeKind.ColorF128, sink, ctx.CancellationToken);
	}
}
