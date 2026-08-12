// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.Mods.Analyzers.BannedApis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssemblyReferenceAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.BannedApis.MonoModAssemblyReferenced,
		Diagnostics.BannedApis.HarmonyAssemblyReferenced
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationAction(analyzeCompilation);
	}

	private static void analyzeCompilation(CompilationAnalysisContext ctx) {
		foreach (IAssemblySymbol asm in ctx.Compilation.SourceModule.ReferencedAssemblySymbols)
			if (KnownMetadataNames.MonoModAssemblies.Contains(asm.Identity.Name))
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.BannedApis.MonoModAssemblyReferenced, Location.None, asm.Identity.Name));
			else if (KnownMetadataNames.HarmonyAssemblies.Contains(asm.Identity.Name))
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.BannedApis.HarmonyAssemblyReferenced, Location.None, asm.Identity.Name));
	}
}
