// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.Analyzers.Core;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodAttributeUsageAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
		Diagnostics.Core.InvalidMethodAttributeUsageTarget,
		Diagnostics.Core.ContradictoryMethodAttributeUsageConstraints,
		Diagnostics.Core.MethodAttributeUsageConstraintViolation
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				KnownTypes known = new(ctx.Compilation);
				if (known.Attribute is null || known.MethodAttributeUsageAttribute is null)
					return;
				ctx.RegisterSymbolAction(c => analyzeNamedType(c, known), SymbolKind.NamedType);
				ctx.RegisterSymbolAction(c => analyzeMethod(c, known), SymbolKind.Method);
			}
		);
	}

	private static void analyzeNamedType(SymbolAnalysisContext ctx, KnownTypes known) {
		if (known.Attribute is null || known.MethodAttributeUsageAttribute is null)
			return;

		var type = (INamedTypeSymbol)ctx.Symbol;
		AttributeData? usage = findAttr(type, known.MethodAttributeUsageAttribute);
		if (usage is null)
			return;

		Location loc = getAttrLocation(usage, type, ctx.CancellationToken);

		if (!type.DerivesFrom(known.Attribute))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.InvalidMethodAttributeUsageTarget, loc, type.ToDisplayString()));

		if (!tryGetConstraints(usage, out MethodConstraintsMirror constraints))
			return;

		string? contradictions = getContradictions(constraints);
		if (contradictions is not null)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.ContradictoryMethodAttributeUsageConstraints, loc, type.ToDisplayString(), contradictions));
	}

	private static void analyzeMethod(SymbolAnalysisContext ctx, KnownTypes known) {
		if (known.Attribute is null || known.MethodAttributeUsageAttribute is null)
			return;

		var method = (IMethodSymbol)ctx.Symbol;
		foreach (AttributeData attr in method.GetAttributes()) {
			INamedTypeSymbol? attrType = attr.AttributeClass;
			if (attrType is null)
				continue;

			AttributeData? usage = findAttr(attrType, known.MethodAttributeUsageAttribute);
			if (usage is null || !tryGetConstraints(usage, out MethodConstraintsMirror constraints))
				continue;

			Location loc = getAttrLocation(attr, method, ctx.CancellationToken);

			string? contradictions = getContradictions(constraints);
			if (contradictions is not null) {
				// source declarations get the diagnostic already from analyzeNamedType, report
				// only if it came from metadata
				if (usage.ApplicationSyntaxReference is null)
					ctx.ReportDiagnostic(
						Diagnostic.Create(
							Diagnostics.Core.ContradictoryMethodAttributeUsageConstraints,
							loc,
							attrType.ToDisplayString(),
							contradictions
						)
					);
				continue;
			}

			string? violations = getViolations(method, constraints);
			if (violations is null)
				continue;
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.MethodAttributeUsageConstraintViolation, loc, attrType.ToDisplayString(), violations));
		}
	}

	private static string? getViolations(IMethodSymbol method, MethodConstraintsMirror constraints) {
		List<string>? violations = null;
		void add(string value) => (violations ??= new List<string>()).Add(value);

		if (has(constraints, MethodConstraintsMirror.Static) && !method.IsStatic)
			add("static");
		if (has(constraints, MethodConstraintsMirror.Instance) && method.IsStatic)
			add("instance");
		if (has(constraints, MethodConstraintsMirror.Public) && method.DeclaredAccessibility != Accessibility.Public)
			add("public");
		if (has(constraints, MethodConstraintsMirror.NonPublic) && method.DeclaredAccessibility == Accessibility.Public)
			add("non-public");
		if (has(constraints, MethodConstraintsMirror.Generic) && !method.IsGenericMethod)
			add("generic");
		if (has(constraints, MethodConstraintsMirror.NonGeneric) && method.IsGenericMethod)
			add("non-generic");
		if (has(constraints, MethodConstraintsMirror.Parameterless) && method.Parameters.Length != 0)
			add("parameterless");
		if (has(constraints, MethodConstraintsMirror.HasParameters) && method.Parameters.Length == 0)
			add("has at least one parameter");
		if (has(constraints, MethodConstraintsMirror.ReturnsVoid) && !method.ReturnsVoid)
			add("returns void");
		if (has(constraints, MethodConstraintsMirror.ReturnsNonVoid) && method.ReturnsVoid)
			add("returns non-void");

		return violations is null ? null : string.Join(", ", violations);
	}

	private static string? getContradictions(MethodConstraintsMirror constraints) {
		List<string>? contradictions = null;
		void check(MethodConstraintsMirror left, MethodConstraintsMirror right, string desc) {
			if (has(constraints, left) && has(constraints, right))
				(contradictions ??= new List<string>()).Add(desc);
		}

		check(MethodConstraintsMirror.Static, MethodConstraintsMirror.Instance, "Static + Instance");
		check(MethodConstraintsMirror.NonPublic, MethodConstraintsMirror.Public, "NonPublic + Public");
		check(MethodConstraintsMirror.NonGeneric, MethodConstraintsMirror.Generic, "NonGeneric + Generic");
		check(MethodConstraintsMirror.Parameterless, MethodConstraintsMirror.HasParameters, "Parameterless + HasParameters");
		check(MethodConstraintsMirror.ReturnsVoid, MethodConstraintsMirror.ReturnsNonVoid, "ReturnsVoid + ReturnsNonVoid");

		return contradictions is null ? null : string.Join("; ", contradictions);
	}

	private static bool tryGetConstraints(AttributeData attr, out MethodConstraintsMirror constraints) {
		constraints = default;
		if (attr.ConstructorArguments.Length != 1)
			return false;
		TypedConstant arg = attr.ConstructorArguments[0];
		if (arg.Kind != TypedConstantKind.Enum || arg.Value is not int raw)
			return false;
		constraints = (MethodConstraintsMirror)raw;
		return true;
	}

	private static AttributeData? findAttr(ISymbol sym, INamedTypeSymbol attrType) {
		foreach (AttributeData attr in sym.GetAttributes())
			if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrType))
				return attr;
		return null;
	}

	private static Location getAttrLocation(AttributeData attr, ISymbol fallbackSymbol, CancellationToken ct) {
		SyntaxNode? sx = attr.ApplicationSyntaxReference?.GetSyntax(ct);
		if (sx is not null)
			return sx.GetLocation();
		foreach (Location loc in fallbackSymbol.Locations)
			if (loc.IsInSource)
				return loc;
		return Location.None;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool has(MethodConstraintsMirror value, MethodConstraintsMirror bit) => (value & bit) != 0;
}
