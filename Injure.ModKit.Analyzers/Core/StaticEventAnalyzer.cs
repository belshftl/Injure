// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.ModKit.Analyzers.Core;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticEventAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Core.StaticEventInNonReloadableMod,
		Diagnostics.Core.StaticEventInReloadableMod
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
			if (!Shared.HotReloadModel.TryGetHotReloadLevel(ctx.Compilation, out var lv))
				return;
			bool nonreloadable = lv < Shared.ModAssemblyHotReloadLevelMirror.SafeBoundary;
			ctx.RegisterSymbolAction(c => analyzeEvent(c, nonreloadable), SymbolKind.Event);
		});
	}

	private static void analyzeEvent(SymbolAnalysisContext ctx, bool nonreloadable) {
		if (!ctx.Symbol.IsStatic)
			return;
		Location? loc = ctx.Symbol.Locations.FirstOrDefault();
		if (loc is null || loc == Location.None)
			return;
		if (nonreloadable)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.StaticEventInNonReloadableMod, loc));
		else
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.StaticEventInReloadableMod, loc));
	}
}
