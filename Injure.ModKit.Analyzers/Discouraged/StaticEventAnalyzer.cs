// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using Injure.ModKit.Analyzers.Shared;

namespace Injure.ModKit.Analyzers.Discouraged;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticEventAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Discouraged.StaticEventDeclaredInNonReloadableMod,
		Diagnostics.Discouraged.StaticEventDeclaredInReloadableMod,
		Diagnostics.Discouraged.StaticEventSubscriptionInReloadableMod
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				if (!HotReloadModel.TryGetHotReloadLevel(ctx.Compilation, out ModAssemblyHotReloadLevelMirror lv))
					return;
				bool nonreloadable = lv < ModAssemblyHotReloadLevelMirror.SafeBoundary;
				ctx.RegisterSymbolAction(c => analyzeEvent(c, nonreloadable), SymbolKind.Event);
				ctx.RegisterOperationAction(c => analyzeEventAssignment(c, nonreloadable), OperationKind.EventAssignment);
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
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.StaticEventDeclaredInNonReloadableMod, loc));
		else
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.StaticEventDeclaredInReloadableMod, loc));
	}

	private static void analyzeEventAssignment(OperationAnalysisContext ctx, bool nonreloadable) {
		if (nonreloadable)
			return;
		var asg = (IEventAssignmentOperation)ctx.Operation;
		var @ref = (IEventReferenceOperation)asg.EventReference;
		IEventSymbol sym = @ref.Event;
		if (!sym.IsStatic)
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.StaticEventSubscriptionInReloadableMod, asg.Syntax.GetLocation()));
	}
}
