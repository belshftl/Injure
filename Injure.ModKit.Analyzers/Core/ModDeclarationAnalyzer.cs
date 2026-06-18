// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Injure.ModKit.Analyzers.Core;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModDeclarationAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
		Diagnostics.Core.MissingModAssemblyAttribute,
		Diagnostics.Core.BadHotReloadLevel,
		Diagnostics.Core.MissingLifetimeIdentityMarker,
		Diagnostics.Core.BadLifetimeIdentityMarkerTarget,
		Diagnostics.Core.DuplicateLifetimeIdentityMarker,
		Diagnostics.Core.MissingEntrypoint,
		Diagnostics.Core.BadEntrypointTarget,
		Diagnostics.Core.DuplicateEntrypoint,
		Diagnostics.Core.MissingReloadEntrypoint,
		Diagnostics.Core.BadReloadEntrypointTarget,
		Diagnostics.Core.DuplicateReloadEntrypoint,
		Diagnostics.Core.DisallowedReloadEntrypoint
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				KnownTypes known = new(ctx.Compilation);
				var model = Model.Create(ctx.Compilation, known);
				if (hasSyntaxTreeDiagnostics(known, model))
					ctx.RegisterSyntaxTreeAction(c => analyzeSyntaxTree(c, ctx.Compilation, known, model));
				if (hasNamedTypeDiagnostics(model))
					ctx.RegisterSymbolAction(c => analyzeNamedType(c, known, model), SymbolKind.NamedType);
			}
		);
	}

	private static bool hasSyntaxTreeDiagnostics(KnownTypes known, Model model) {
		if (known.ModAssemblyAttribute is null || model.ModAssemblyAttributes.Length == 0)
			return true;
		if (model.ModAssemblyAttributes.Length == 1 && model.ModAssembly is not null && !Model.IsEnumValueNamed(known.ModAssemblyHotReloadLevel, model.ModAssembly.HotReloadRawValue))
			return true;
		if (known.ModLifetimeIdentityMarkerAttribute is null || model.LifetimeCandidates.Length == 0)
			return true;
		if (known.ModEntrypointAttribute is null || model.LifetimeIdentity is null || model.EntrypointCandidates.Length == 0)
			return true;
		if (known.ModReloadEntrypointAttribute is not null && model.LifetimeIdentity is not null && model.Entrypoint is not null && (model.ModAssembly?.IsLive ?? false) &&
			model.ReloadEntrypointCandidates.Length == 0)
			return true;
		return false;
	}

	private static bool hasNamedTypeDiagnostics(Model model) =>
		model.LifetimeCandidates.Length != 0 || model.EntrypointCandidates.Length != 0 || model.ReloadEntrypointCandidates.Length != 0;

	private static void analyzeSyntaxTree(SyntaxTreeAnalysisContext ctx, Compilation compilation, KnownTypes known, Model model) {
		analyzeModAssembly(ctx, compilation, known, model);
		analyzeMissingLifetimeIdentity(ctx, compilation, known, model);
		analyzeMissingEntrypoint(ctx, compilation, known, model);
		analyzeMissingReloadEntrypoint(ctx, compilation, known, model);
	}

	private static void analyzeNamedType(SymbolAnalysisContext ctx, KnownTypes known, Model model) {
		var type = (INamedTypeSymbol)ctx.Symbol;
		analyzeLifetimeIdentity(ctx, known, model, type);
		analyzeEntrypoint(ctx, known, model, type);
		analyzeReloadEntrypoint(ctx, known, model, type);
	}

	private static void analyzeModAssembly(SyntaxTreeAnalysisContext ctx, Compilation compilation, KnownTypes known, Model model) {
		if (known.ModAssemblyAttribute is null || model.ModAssemblyAttributes.Length == 0)
			if (isStableReportingTree(compilation, ctx.Tree))
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.MissingModAssemblyAttribute, getStableTreeLocation(ctx.Tree)));
		if (model.ModAssemblyAttributes.Length == 1 && model.ModAssembly is not null &&
			!Model.IsEnumValueNamed(known.ModAssemblyHotReloadLevel, model.ModAssembly.HotReloadRawValue)) {
			Location location = model.ModAssembly.Location;
			if (shouldReportOnTree(compilation, ctx.Tree, location))
				ctx.ReportDiagnostic(
					Diagnostic.Create(Diagnostics.Core.BadHotReloadLevel, getTreeDiagnosticLocation(ctx.Tree, location), model.ModAssembly.HotReloadRawValue)
				);
		}
	}

	private static void analyzeMissingLifetimeIdentity(SyntaxTreeAnalysisContext ctx, Compilation compilation, KnownTypes known, Model model) {
		if (known.ModLifetimeIdentityMarkerAttribute is not null && model.LifetimeCandidates.Length != 0)
			return;
		if (isStableReportingTree(compilation, ctx.Tree))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.MissingLifetimeIdentityMarker, getStableTreeLocation(ctx.Tree)));
	}

	private static void analyzeMissingEntrypoint(SyntaxTreeAnalysisContext ctx, Compilation compilation, KnownTypes known, Model model) {
		if (known.ModEntrypointAttribute is not null && model.LifetimeIdentity is not null && model.EntrypointCandidates.Length != 0)
			return;
		if (isStableReportingTree(compilation, ctx.Tree))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.MissingEntrypoint, getStableTreeLocation(ctx.Tree)));
	}

	private static void analyzeMissingReloadEntrypoint(SyntaxTreeAnalysisContext ctx, Compilation compilation, KnownTypes known, Model model) {
		if (known.ModReloadEntrypointAttribute is null || model.LifetimeIdentity is null || model.Entrypoint is null)
			return;
		if (!(model.ModAssembly?.IsLive ?? false) || model.ReloadEntrypointCandidates.Length != 0)
			return;
		if (isStableReportingTree(compilation, ctx.Tree))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.MissingReloadEntrypoint, getStableTreeLocation(ctx.Tree)));
	}

	private static void analyzeLifetimeIdentity(SymbolAnalysisContext ctx, KnownTypes known, Model model, INamedTypeSymbol type) {
		if (!contains(model.LifetimeCandidates, type))
			return;
		if (model.LifetimeCandidates.Length > 1)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.DuplicateLifetimeIdentityMarker, SymbolHelpers.GetBestLocation(type), type.ToDisplayString()));
		if (
			type.TypeKind != TypeKind.Struct || !type.IsReadOnly ||
			!type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, known.ModLifetimeIdentityInterface)) ||
			type.GetMembers().Any(static m => !m.IsImplicitlyDeclared)
		)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.BadLifetimeIdentityMarkerTarget, SymbolHelpers.GetBestLocation(type), type.ToDisplayString()));
	}

	private static void analyzeEntrypoint(SymbolAnalysisContext ctx, KnownTypes known, Model model, INamedTypeSymbol type) {
		if (known.ModEntrypointAttribute is null || model.LifetimeIdentity is null || model.EntrypointCandidates.Length == 0)
			return;
		if (!contains(model.EntrypointCandidates, type))
			return;
		if (model.EntrypointCandidates.Length > 1)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.DuplicateEntrypoint, SymbolHelpers.GetBestLocation(type), type.ToDisplayString()));
		validateEntrypointCandidate(ctx, known.ModEntrypointInterface, model.LifetimeIdentity.Type, type);
	}

	private static void analyzeReloadEntrypoint(SymbolAnalysisContext ctx, KnownTypes known, Model model, INamedTypeSymbol type) {
		if (known.ModReloadEntrypointAttribute is null || model.LifetimeIdentity is null || model.Entrypoint is null)
			return;
		if (!contains(model.ReloadEntrypointCandidates, type))
			return;
		if (!(model.ModAssembly?.IsLive ?? false)) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.DisallowedReloadEntrypoint, SymbolHelpers.GetBestLocation(type)));
			return;
		}
		if (model.ReloadEntrypointCandidates.Length > 1)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.DuplicateReloadEntrypoint, SymbolHelpers.GetBestLocation(type), type.ToDisplayString()));
		validateReloadEntrypointCandidate(ctx, known.ModReloadEntrypointInterface, model.LifetimeIdentity.Type, model.Entrypoint.GameApiType, type);
	}

	private static void validateEntrypointCandidate(
		SymbolAnalysisContext ctx,
		INamedTypeSymbol? openInterface,
		INamedTypeSymbol lifetimeType,
		INamedTypeSymbol candidate
	) {
		INamedTypeSymbol? iface = findClosedInterface(candidate, openInterface);
		if (
			candidate.TypeKind != TypeKind.Class || candidate.IsGenericType || !candidate.IsSealed || candidate.IsAbstract ||
			iface is null || !SymbolEqualityComparer.Default.Equals(iface.TypeArguments[1], lifetimeType)
		)
			ctx.ReportDiagnostic(
				Diagnostic.Create(Diagnostics.Core.BadEntrypointTarget, SymbolHelpers.GetBestLocation(candidate), candidate.ToDisplayString(), lifetimeType.ToDisplayString())
			);
	}

	private static void validateReloadEntrypointCandidate(
		SymbolAnalysisContext ctx,
		INamedTypeSymbol? openInterface,
		INamedTypeSymbol lifetimeType,
		ITypeSymbol gameApiType,
		INamedTypeSymbol candidate
	) {
		INamedTypeSymbol? iface = findClosedInterface(candidate, openInterface);
		if (
			candidate.TypeKind != TypeKind.Class || candidate.IsGenericType || !candidate.IsSealed || candidate.IsAbstract ||
			iface is null || !SymbolEqualityComparer.Default.Equals(iface.TypeArguments[1], lifetimeType) ||
			!SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], gameApiType)
		)
			ctx.ReportDiagnostic(
				Diagnostic.Create(
					Diagnostics.Core.BadReloadEntrypointTarget,
					SymbolHelpers.GetBestLocation(candidate),
					candidate.ToDisplayString(),
					gameApiType.ToDisplayString(),
					lifetimeType.ToDisplayString()
				)
			);
	}

	private static INamedTypeSymbol? findClosedInterface(INamedTypeSymbol type, INamedTypeSymbol? openInterface) {
		if (openInterface is null)
			return null;
		foreach (INamedTypeSymbol iface in type.AllInterfaces)
			if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, openInterface))
				return iface;
		return null;
	}

	private static bool contains(ImmutableArray<INamedTypeSymbol> types, INamedTypeSymbol type) {
		foreach (INamedTypeSymbol candidate in types)
			if (SymbolEqualityComparer.Default.Equals(candidate, type))
				return true;
		return false;
	}

	private static bool shouldReportOnTree(Compilation compilation, SyntaxTree tree, Location? preferredLocation) =>
		preferredLocation?.SourceTree is SyntaxTree preferredTree ? preferredTree == tree : isStableReportingTree(compilation, tree);

	private static Location getTreeDiagnosticLocation(SyntaxTree tree, Location? preferredLocation) =>
		preferredLocation is { SourceTree: SyntaxTree preferredTree } && preferredTree == tree ? preferredLocation : getStableTreeLocation(tree);

	private static Location getStableTreeLocation(SyntaxTree tree) => Location.Create(tree, new TextSpan(0, 0));

	private static bool isStableReportingTree(Compilation compilation, SyntaxTree tree) {
		SyntaxTree? firstTree = null;
		foreach (SyntaxTree candidate in compilation.SyntaxTrees) {
			firstTree ??= candidate;
			if (!isGeneratedTree(candidate))
				return candidate == tree;
		}
		return firstTree == tree;
	}

	private static bool isGeneratedTree(SyntaxTree tree) {
		string path = tree.FilePath;
		if (string.IsNullOrEmpty(path))
			return false;
		return path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
	}
}
