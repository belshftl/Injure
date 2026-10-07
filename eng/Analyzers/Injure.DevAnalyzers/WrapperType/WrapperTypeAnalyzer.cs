// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.WrapperType;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WrapperTypeAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.WrapperType.WrapperTypeInvalidTarget,
		Diagnostics.WrapperType.WrapperTypeMustBeReadonly,
		Diagnostics.WrapperType.WrapperTypeInvalidSourceShape,
		Diagnostics.WrapperType.WrapperTypeInvalidWrappedType,
		Diagnostics.WrapperType.WrapperTypeInvalidMember
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(analyze, SymbolKind.NamedType);
	}

	private static void analyze(SymbolAnalysisContext ctx) {
		var sym = (INamedTypeSymbol)ctx.Symbol;
		AttributeData? attr = Util.GetAttribute(sym, Constants.WrapperType.AttributeMetadataName);
		if (attr is not null)
			WrapperTypeModel.Validate(sym, attr, ctx.Compilation, new DiagnosticSink(ctx.ReportDiagnostic), ctx.CancellationToken);
	}
}
