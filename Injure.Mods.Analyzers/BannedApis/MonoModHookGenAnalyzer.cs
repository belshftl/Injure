// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.Mods.Analyzers.BannedApis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MonoModHookGenAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.BannedApis.MonoModUsed
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				ctx.RegisterOperationAction(analyzeEventAssignment, OperationKind.EventAssignment);
			}
		);
	}

	private static void analyzeEventAssignment(OperationAnalysisContext ctx) {
		var asg = (IEventAssignmentOperation)ctx.Operation;
		var @ref = (IEventReferenceOperation)asg.EventReference;
		IEventSymbol sym = @ref.Event;
		INamespaceSymbol ns = sym.ContainingNamespace;
		for (; ns.ContainingNamespace is { IsGlobalNamespace: false } parent; ns = parent);
		if (ns.Name is "On" or "IL")
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.BannedApis.MonoModUsed, asg.Syntax.GetLocation()));
	}
}
