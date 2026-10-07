// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.ClosedEnum;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClosedEnumAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.ClosedEnum.ClosedEnumInvalidTarget,
		Diagnostics.ClosedEnum.ClosedEnumMustBeReadonly,
		Diagnostics.ClosedEnum.ClosedEnumInvalidSourceShape,
		Diagnostics.ClosedEnum.ClosedEnumInvalidCaseEnum,
		Diagnostics.ClosedEnum.ClosedEnumAliasNotSupported,
		Diagnostics.ClosedEnum.ClosedEnumDefaultRule,
		Diagnostics.ClosedEnum.ClosedEnumSuspiciousZeroName,
		Diagnostics.ClosedEnum.ClosedEnumMirrorInvalid,
		Diagnostics.ClosedEnum.ClosedEnumMirrorMismatch
	);

	public override void Initialize(AnalysisContext ctx) {
		ctx.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		ctx.EnableConcurrentExecution();
		ctx.RegisterSymbolAction(analyze, SymbolKind.NamedType);
	}

	private static void analyze(SymbolAnalysisContext ctx) {
		var sym = (INamedTypeSymbol)ctx.Symbol;
		AttributeData? attr = Util.GetAttribute(sym, Constants.ClosedEnum.AttributeMetadataName);
		if (attr is not null)
			ClosedEnumModel.Validate(sym, attr, new DiagnosticSink(ctx.ReportDiagnostic), ctx.CancellationToken);
	}
}
