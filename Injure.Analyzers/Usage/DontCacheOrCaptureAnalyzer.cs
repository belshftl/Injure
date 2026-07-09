// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.Analyzers.Usage;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DontCacheAnalyzer : DiagnosticAnalyzer {
	private readonly struct RestrictionInfo(string why, ITypeSymbol markedType) {
		public readonly string Why = why;
		public readonly ITypeSymbol MarkedType = markedType;
	}

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Usage.DontCacheObject,
		Diagnostics.Usage.DontCaptureObjectIntoClosure
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(ctx => {
			KnownTypes known = new(ctx.Compilation);
			ctx.RegisterSymbolAction(c => analyzeField(c, known), SymbolKind.Field);
			ctx.RegisterSymbolAction(c => analyzeProperty(c, known), SymbolKind.Property);
			ctx.RegisterOperationAction(c => analyzeAnonymousFunction(c, known), OperationKind.AnonymousFunction);
			ctx.RegisterOperationAction(c => analyzeLocalFunction(c, known), OperationKind.LocalFunction);
		});
	}

	private static void analyzeField(SymbolAnalysisContext ctx, KnownTypes known) {
		var field = (IFieldSymbol)ctx.Symbol;
		if (field.IsImplicitlyDeclared)
			return;
		if (!tryFindRestrictedType(field.Type, known, closure: false, out RestrictionInfo info))
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.Usage.DontCacheObject,
			field.Locations.FirstOrDefault(static l => l.IsInSource),
			field.Type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
			info.Why
		));
	}

	private static void analyzeProperty(SymbolAnalysisContext ctx, KnownTypes known) {
		var property = (IPropertySymbol)ctx.Symbol;
		if (property.IsImplicitlyDeclared)
			return;
		if (!tryFindRestrictedType(property.Type, known, closure: false, out RestrictionInfo info))
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.Usage.DontCacheObject,
			property.Locations.FirstOrDefault(static l => l.IsInSource),
			property.Type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
			info.Why
		));
	}

	private static void analyzeAnonymousFunction(OperationAnalysisContext ctx, KnownTypes known) {
		var anon = (IAnonymousFunctionOperation)ctx.Operation;
		ClosureCaptureWalker walker = new(anon.Symbol, known, ctx.ReportDiagnostic, ctx.CancellationToken);
		walker.Visit(anon.Body);
	}

	private static void analyzeLocalFunction(OperationAnalysisContext ctx, KnownTypes known) {
		var local = (ILocalFunctionOperation)ctx.Operation;
		ClosureCaptureWalker walker = new(local.Symbol, known, ctx.ReportDiagnostic, ctx.CancellationToken);
		walker.Visit(local.Body);
	}

	private sealed class ClosureCaptureWalker(
		IMethodSymbol closureSymbol,
		KnownTypes wellKnown,
		Action<Diagnostic> report,
		CancellationToken cancellationToken
	) : OperationWalker {
		private readonly IMethodSymbol closureSymbol = closureSymbol;
		private readonly KnownTypes known = wellKnown;
		private readonly Action<Diagnostic> report = report;
		private readonly CancellationToken cancellationToken = cancellationToken;
		private readonly HashSet<ISymbol> reportedSymbols = new(SymbolEqualityComparer.Default);
		private bool reportedThis;

		public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation) {
			// nested closures get their own operation action
		}

		public override void VisitLocalFunction(ILocalFunctionOperation operation) {
			// nested closures get their own operation action
		}

		public override void VisitLocalReference(ILocalReferenceOperation operation) {
			cancellationToken.ThrowIfCancellationRequested();
			if (!SymbolEqualityComparer.Default.Equals(operation.Local.ContainingSymbol, closureSymbol))
				reportCapturedSymbol(operation.Local, operation.Local.Type, operation.Syntax.GetLocation());
			base.VisitLocalReference(operation);
		}

		public override void VisitParameterReference(IParameterReferenceOperation operation) {
			cancellationToken.ThrowIfCancellationRequested();
			if (!SymbolEqualityComparer.Default.Equals(operation.Parameter.ContainingSymbol, closureSymbol))
				reportCapturedSymbol(operation.Parameter, operation.Parameter.Type, operation.Syntax.GetLocation());
			base.VisitParameterReference(operation);
		}

		public override void VisitInstanceReference(IInstanceReferenceOperation operation) {
			cancellationToken.ThrowIfCancellationRequested();
			if (!reportedThis && operation.Type is { } type && tryFindRestrictedType(type, known, closure: true, out RestrictionInfo info)) {
				report(Diagnostic.Create(
					Diagnostics.Usage.DontCaptureObjectIntoClosure,
					operation.Syntax.GetLocation(),
					info.MarkedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
					info.Why
				));
				reportedThis = true;
			}
			base.VisitInstanceReference(operation);
		}

		private void reportCapturedSymbol(ISymbol symbol, ITypeSymbol type, Location location) {
			if (!reportedSymbols.Add(symbol))
				return;
			if (!tryFindRestrictedType(type, known, closure: true, out RestrictionInfo info))
				return;
			report(Diagnostic.Create(
				Diagnostics.Usage.DontCaptureObjectIntoClosure,
				location,
				info.MarkedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
				info.Why
			));
		}
	}

	private static bool tryFindRestrictedType(ITypeSymbol type, KnownTypes known, bool closure, out RestrictionInfo info) {
		info = default;

		INamedTypeSymbol? attr = closure ? known.DontCaptureIntoClosureAttribute : known.DontCacheAttribute;
		if (attr is null)
			return false;

		if (tryFindDirectlyMarkedType(type, attr, out info))
			return true;
		if (type is INamedTypeSymbol named && tryFindSupportedCollectionElement(named, known, attr, out info))
			return true;
		foreach (INamedTypeSymbol iface in type.AllInterfaces)
			if (tryFindSupportedCollectionElement(iface, known, attr, out info))
				return true;
		info = default;
		return false;
	}

	private static bool tryFindSupportedCollectionElement(INamedTypeSymbol type, KnownTypes known, INamedTypeSymbol attr, out RestrictionInfo info) {
		info = default;

		if (
			isConstructedFrom(type, known.IReadOnlyCollection) &&
			type.TypeArguments.Length == 1 &&
			tryFindDirectlyMarkedType(type.TypeArguments[0], attr, out info)
		)
			return true;

		if (
			isConstructedFrom(type, known.ICollection) &&
			type.TypeArguments.Length == 1 &&
			tryFindDirectlyMarkedType(type.TypeArguments[0], attr, out info)
		)
			return true;

		if (
			isConstructedFrom(type, known.IReadOnlyDictionary) &&
			type.TypeArguments.Length == 2
		) {
			if (tryFindDirectlyMarkedType(type.TypeArguments[0], attr, out info))
				return true;
			if (tryFindDirectlyMarkedType(type.TypeArguments[1], attr, out info))
				return true;
		}

		return false;
	}

	private static bool tryFindDirectlyMarkedType(ITypeSymbol type, INamedTypeSymbol attr, out RestrictionInfo info) {
		type = unwrapNullableOfT(type);
		foreach (AttributeData a in type.GetAttributes()) {
			if (a.AttributeClass is null || !SymbolEqualityComparer.Default.Equals(a.AttributeClass.OriginalDefinition, attr.OriginalDefinition))
				continue;
			string why = a.ConstructorArguments is [{ Value: string s }] ? s : "<unknown reason>";
			info = new RestrictionInfo(why, type);
			return true;
		}
		info = default;
		return false;
	}

	private static bool isConstructedFrom(INamedTypeSymbol type, [NotNullWhen(true)] INamedTypeSymbol? genericDefinition) =>
		genericDefinition is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, genericDefinition);

	private static ITypeSymbol unwrapNullableOfT(ITypeSymbol type) {
		if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && named.TypeArguments.Length == 1)
			return named.TypeArguments[0];
		return type;
	}
}
