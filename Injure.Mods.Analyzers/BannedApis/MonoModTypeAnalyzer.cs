// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.Mods.Analyzers.BannedApis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MonoModTypeAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.BannedApis.MonoModUsed
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				ctx.RegisterSymbolAction(analyzeField, SymbolKind.Field);
				ctx.RegisterSymbolAction(analyzeProperty, SymbolKind.Property);
				ctx.RegisterOperationAction(analyzeObjectCreation, OperationKind.ObjectCreation);
				ctx.RegisterOperationAction(analyzeInvocation, OperationKind.Invocation);
			}
		);
	}

	// TODO check arrays / pointers / collections

	private static void analyzeField(SymbolAnalysisContext ctx) {
		var field = (IFieldSymbol)ctx.Symbol;
		if (field.IsImplicitlyDeclared)
			return;
		if (field.Type.ContainingNamespace is not { } ns)
			return;
		for (; ns.ContainingNamespace is { IsGlobalNamespace: false } parent; ns = parent);
		if (ns.Name is KnownMetadataNames.MonoModRootNamespace)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.BannedApis.MonoModUsed, field.Locations.FirstOrDefault(static l => l.IsInSource)));
	}

	private static void analyzeProperty(SymbolAnalysisContext ctx) {
		var property = (IPropertySymbol)ctx.Symbol;
		if (property.IsImplicitlyDeclared)
			return;
		if (property.Type.ContainingNamespace is not { } ns)
			return;
		for (; ns.ContainingNamespace is { IsGlobalNamespace: false } parent; ns = parent);
		if (ns.Name is KnownMetadataNames.MonoModRootNamespace)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.BannedApis.MonoModUsed, property.Locations.FirstOrDefault(static l => l.IsInSource)));
	}

	private static void analyzeObjectCreation(OperationAnalysisContext ctx) {
		var creat = (IObjectCreationOperation)ctx.Operation;
		if (creat.Type is not ITypeSymbol type)
			return;
		if (type.ContainingNamespace is not { } ns)
			return;
		for (; ns.ContainingNamespace is { IsGlobalNamespace: false } parent; ns = parent);
		if (ns.Name is KnownMetadataNames.MonoModRootNamespace)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.BannedApis.MonoModUsed, creat.Syntax.GetLocation()));
	}

	private static void analyzeInvocation(OperationAnalysisContext ctx) {
		var inv = (IInvocationOperation)ctx.Operation;
		IMethodSymbol method = inv.TargetMethod.ReducedFrom ?? inv.TargetMethod;
		if (method.ContainingNamespace is not { } ns)
			return;
		for (; ns.ContainingNamespace is { IsGlobalNamespace: false } parent; ns = parent);
		if (ns.Name is KnownMetadataNames.MonoModRootNamespace)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.BannedApis.MonoModUsed, inv.Syntax.GetLocation()));
	}
}
