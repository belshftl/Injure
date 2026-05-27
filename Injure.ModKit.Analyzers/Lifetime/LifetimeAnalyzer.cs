// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.ModKit.Analyzers.Lifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LifetimeAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Lifetime.LifetimeObligationLeaked,
		Diagnostics.Lifetime.LifetimeObligationExceptionLeaked,
		Diagnostics.Lifetime.AsyncCallNeedsBoundedToken,
		Diagnostics.Lifetime.AnalysisBailout
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(startCompilation);
	}

	private static void startCompilation(CompilationStartAnalysisContext ctx) {
		if (!Shared.HotReloadModel.TryGetHotReloadLevel(ctx.Compilation, out var lv) || lv < Shared.ModAssemblyHotReloadLevelMirror.SafeBoundary)
			return;
		KnownTypes known = new(ctx.Compilation);
		ctx.RegisterOperationBlockAction(blockCtx => {
			if (blockCtx.OperationBlocks.Length == 0)
				return;

			IOperation body = blockCtx.OperationBlocks[0];
			BoundedTokenProvenance tokenProvenance = new(known);
			LifetimeRuleSet rules = new(known, tokenProvenance);
			MethodAnalyzer analyzer = new(known, rules, tokenProvenance);
			MethodLifetimeResult result = analyzer.Analyze(body);

			if (result.AnalysisBailedOut && result.BailoutLocation is not null) {
				blockCtx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Lifetime.AnalysisBailout, result.BailoutLocation, result.BailoutReason ?? "unsupported control flow"));
				return;
			}

			foreach (ObligationTransitionEvent ev in result.Events) {
				switch (ev.NewState) {
				case ObligationState.Leaked:
					blockCtx.ReportDiagnostic(Diagnostic.Create(
						Diagnostics.Lifetime.LifetimeObligationLeaked,
						ev.Location,
						ev.SubjectName,             // object '{0}' ...
						ev.Kind,                    // with obligation '{1}' ...
						ev.Cause,                   // leaked here by '{2}' ...
						ev.RequiredSatisfaction,    // obligation must be satisfied by at least '{3}' ...
						ev.BestObservedSatisfaction // found '{4}'
					));
					break;
				case ObligationState.ExceptionLeaked:
					blockCtx.ReportDiagnostic(Diagnostic.Create(
						Diagnostics.Lifetime.LifetimeObligationExceptionLeaked,
						ev.Location,
						ev.SubjectName,             // object '{0}' ...
						ev.Kind,                    // with obligation '{1}' may leak if this statement throws ...
						ev.RequiredSatisfaction,    // obligation must be satisfied by at least '{2}' ...
						ev.BestObservedSatisfaction // found '{3}'
					));
					break;
				default: continue;
				}
			}

			foreach (AsyncTokenWarning w in result.AsyncTokenWarnings)
				blockCtx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Lifetime.AsyncCallNeedsBoundedToken, w.Location, w.TargetMethod.Name));
		});
	}
}
