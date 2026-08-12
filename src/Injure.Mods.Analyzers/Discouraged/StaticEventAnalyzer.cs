// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods.Analyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.Mods.Analyzers.Discouraged;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticEventAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Discouraged.StaticEventSubscriptionInReloadableMod
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				if (!HotReloadModel.TryGetHotReloadLevel(ctx.Compilation, out ModAssemblyHotReloadLevelMirror lv))
					return;
				if (lv >= ModAssemblyHotReloadLevelMirror.SafeBoundary)
					ctx.RegisterOperationAction(analyzeEventAssignment, OperationKind.EventAssignment);
			}
		);
	}

	private static void analyzeEventAssignment(OperationAnalysisContext ctx) {
		var asg = (IEventAssignmentOperation)ctx.Operation;
		var @ref = (IEventReferenceOperation)asg.EventReference;
		IEventSymbol sym = @ref.Event;
		if (sym.IsStatic)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.StaticEventSubscriptionInReloadableMod, asg.Syntax.GetLocation()));
	}
}
