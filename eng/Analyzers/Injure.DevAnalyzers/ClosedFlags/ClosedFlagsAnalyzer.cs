// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.ClosedFlags;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClosedFlagsAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.ClosedFlags.ClosedFlagsInvalidTarget,
		Diagnostics.ClosedFlags.ClosedFlagsMustBeReadonly,
		Diagnostics.ClosedFlags.ClosedFlagsInvalidSourceShape,
		Diagnostics.ClosedFlags.ClosedFlagsInvalidBitsEnum,
		Diagnostics.ClosedFlags.ClosedFlagsAliasNotSupported,
		Diagnostics.ClosedFlags.ClosedFlagsBadMemberValue,
		Diagnostics.ClosedFlags.ClosedFlagsDefaultRule,
		Diagnostics.ClosedFlags.ClosedFlagsSuspiciousZeroName,
		Diagnostics.ClosedFlags.ClosedFlagsMirrorInvalid,
		Diagnostics.ClosedFlags.ClosedFlagsMirrorMismatch
	);

	public override void Initialize(AnalysisContext ctx) {
		ctx.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		ctx.EnableConcurrentExecution();
		ctx.RegisterSymbolAction(analyze, SymbolKind.NamedType);
	}

	private static void analyze(SymbolAnalysisContext ctx) {
		var sym = (INamedTypeSymbol)ctx.Symbol;
		AttributeData? attr = Util.GetAttribute(sym, Constants.ClosedFlags.AttributeMetadataName);
		if (attr is not null)
			ClosedFlagsModel.Validate(sym, attr, new DiagnosticSink(ctx.ReportDiagnostic), ctx.CancellationToken);
	}
}
