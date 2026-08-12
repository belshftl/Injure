// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.StronglyTypedInt;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StronglyTypedIntAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget,
		Diagnostics.StronglyTypedInt.StronglyTypedIntMustBeReadonly,
		Diagnostics.StronglyTypedInt.StronglyTypedIntUnsupportedBacking,
		Diagnostics.StronglyTypedInt.StronglyTypedIntMemberCollision
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(analyzeNamedType, SymbolKind.NamedType);
	}

	private static void analyzeNamedType(SymbolAnalysisContext ctx) {
		var sym = (INamedTypeSymbol)ctx.Symbol;
		AttributeData? attr = Util.GetAttribute(sym, AttributeSources.StronglyTypedIntAttributeMetadataName);
		if (attr is null)
			return;

		Location loc = Util.GetAttributeLocation(attr, sym, ctx.CancellationToken);
		if (sym.TypeKind != TypeKind.Struct)
			report(ctx, Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "Target must be a struct.");
		else if (!Util.Partial(sym, ctx.CancellationToken))
			report(ctx, Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "Target must be declared 'partial'.");
		else if (!sym.IsReadOnly)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.StronglyTypedInt.StronglyTypedIntMustBeReadonly, loc, sym.Name));
		else if (sym.IsRefLikeType)
			report(ctx, Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "Ref structs are not supported.");
		else if (sym.IsRecord)
			report(ctx, Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "'record struct' is not supported.");
		else if (sym.TypeParameters.Length != 0)
			report(ctx, Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "Generic structs are not supported.");
		else if (attr.ConstructorArguments.Length != 1)
			report(ctx, Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "Attribute must have exactly one typeof(...) argument.");
		else if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol backingType)
			report(ctx, Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "Attribute argument must be a concrete type.");
		else if (!Util.TryGetStronglyTypedIntBackingInfo(backingType, out _, out _))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.StronglyTypedInt.StronglyTypedIntUnsupportedBacking, loc, backingType.ToDisplayString()));
		else if (Util.CheckStronglyTypedIntCollision(sym, backingType, out Location collisionLoc, out string? collisionMsg))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.StronglyTypedInt.StronglyTypedIntMemberCollision, collisionLoc, collisionMsg));
	}
	private static void report(SymbolAnalysisContext ctx, DiagnosticDescriptor descriptor, Location loc, string msg) =>
		ctx.ReportDiagnostic(Diagnostic.Create(descriptor, loc, msg));
}
