// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.ModKit.Analyzers.Discouraged;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HookMemberAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Discouraged.HookFieldInReloadableMod
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
			if (!Shared.HotReloadModel.TryGetHotReloadLevel(ctx.Compilation, out var lv) || lv < Shared.ModAssemblyHotReloadLevelMirror.SafeBoundary)
				return;
			KnownSymbols known = new(ctx.Compilation);
			ctx.RegisterSymbolAction(c => analyzeField(c, known), SymbolKind.Field);
			ctx.RegisterSymbolAction(c => analyzeProperty(c, known), SymbolKind.Property);
		});
	}

	private static void analyzeField(SymbolAnalysisContext ctx, KnownSymbols known) {
		var field = (IFieldSymbol)ctx.Symbol;
		if (field.IsImplicitlyDeclared)
			return;
		if (field.Type.IsHook(known))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.HookFieldInReloadableMod, field.Locations.FirstOrDefault()));
	}

	private static void analyzeProperty(SymbolAnalysisContext ctx, KnownSymbols known) {
		var property = (IPropertySymbol)ctx.Symbol;
		if (property.IsImplicitlyDeclared)
			return;
		if (property.Type.IsHook(known))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.HookFieldInReloadableMod, property.Locations.FirstOrDefault()));
	}
}
