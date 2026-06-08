// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;

using Injure.ModKit.Analyzers.Shared;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.ModKit.Analyzers.Discouraged;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticEventAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Discouraged.StaticEventInNonReloadableMod,
		Diagnostics.Discouraged.StaticEventInReloadableMod
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				if (!HotReloadModel.TryGetHotReloadLevel(ctx.Compilation, out ModAssemblyHotReloadLevelMirror lv))
					return;
				bool nonreloadable = lv < ModAssemblyHotReloadLevelMirror.SafeBoundary;
				ctx.RegisterSymbolAction(c => analyzeEvent(c, nonreloadable), SymbolKind.Event);
			}
		);
	}

	private static void analyzeEvent(SymbolAnalysisContext ctx, bool nonreloadable) {
		if (!ctx.Symbol.IsStatic)
			return;
		Location? loc = ctx.Symbol.Locations.FirstOrDefault();
		if (loc is null || loc == Location.None)
			return;
		if (nonreloadable)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.StaticEventInNonReloadableMod, loc));
		else
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.StaticEventInReloadableMod, loc));
	}
}
