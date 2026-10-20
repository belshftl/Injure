// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.AnalyzerShared.Usage;

// args:
//   - created: {0} = type name
//   - optionalParameter: {0} = parameter name, {1} = type name
//   - field: {0} = field name, {1} = field type name, {2} = containing type name
public abstract class InvalidDefaultAnalyzerBase(
	DiagnosticDescriptor created,
	DiagnosticDescriptor optionalParameter,
	DiagnosticDescriptor field
) : DiagnosticAnalyzer {
	private const string interfaceName = "Injure.IHasInvalidDefault";

	public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
		created,
		optionalParameter,
		field
	);

	public sealed override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(start => {
			if (start.Compilation.GetTypeByMetadataName(interfaceName) is not INamedTypeSymbol iface)
				return;
			start.RegisterOperationAction(
				c => analyzeCreation(c, iface),
				OperationKind.DefaultValue,
				OperationKind.ObjectCreation,
				OperationKind.TypeParameterObjectCreation
			);
			start.RegisterSymbolAction(c => analyzeMethod(c, iface), SymbolKind.Method);
			start.RegisterSymbolAction(c => analyzeProperty(c, iface), SymbolKind.Property);
			start.RegisterOperationAction(c => analyzeLocalFunction(c, iface), OperationKind.LocalFunction, OperationKind.AnonymousFunction);
			start.RegisterSymbolAction(c => analyzeStruct(c, iface), SymbolKind.NamedType);
		});
	}

	// whether `type` implements `iface` or is a type parameter constrained to it
	private static bool hasInvalidDefault(ITypeSymbol? type, INamedTypeSymbol iface) {
		switch (type) {
		case ITypeParameterSymbol tp:
			foreach (ITypeSymbol constraint in tp.ConstraintTypes)
				if (SymbolEqualityComparer.Default.Equals(constraint, iface) || hasInvalidDefault(constraint, iface))
					return true;
			return false;
		case INamedTypeSymbol { TypeKind: TypeKind.Struct } named:
			return named.AllInterfaces.Contains(iface, SymbolEqualityComparer.Default);
		default:
			return false;
		}
	}

	// ==========================================================================
	// creating default values
	private void analyzeCreation(OperationAnalysisContext ctx, INamedTypeSymbol iface) {
		IOperation op = ctx.Operation;
		switch (op) {
		case IDefaultValueOperation:
			// omitted optional arguments; report the parameter declaration instead
			if (op.IsImplicit)
				return;
			break;
		case IObjectCreationOperation creation:
			// an object initializer doesn't count, since nothing here knows which members IsDefault
			// reads; an explicit parameterless constructor does
			if (creation.Constructor is not { IsImplicitlyDeclared: true, Parameters.Length: 0 })
				return;
			break;
		case ITypeParameterObjectCreationOperation:
			break;
		default:
			return;
		}
		if (
			!hasInvalidDefault(op.Type, iface)
			|| isComparison(op)
			|| isOutAssignment(op)
			|| outermostConversion(op).Parent is IParameterInitializerOperation // reported as an optional parameter
			|| isInside(ctx.ContainingSymbol, op.Type!)
		)
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(created, op.Syntax.GetLocation(), display(op.Type!)));
	}

	private static IOperation outermostConversion(IOperation op) {
		while (op.Parent is IConversionOperation { IsImplicit: true } conv)
			op = conv;
		return op;
	}

	// `x == default` / `x != default`
	private static bool isComparison(IOperation op) =>
		outermostConversion(op).Parent is IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals };

	// `v = default;` for an out parameter, as in the usual Try* pattern, or ternary / null-coalescing
	// branches, i.e. `v = cond ? x : default;` and `v = x ?? default;`
	private static bool isOutAssignment(IOperation op) {
		IOperation outer = outermostConversion(op);
		for (;;) {
			if (outer.Parent is IConditionalOperation cond && (cond.WhenTrue == outer || cond.WhenFalse == outer))
				outer = outermostConversion(cond);
			else if (outer.Parent is ICoalesceOperation csc && csc.WhenNull == outer)
				outer = outermostConversion(csc);
			else
				break;
		}
		return outer.Parent is ISimpleAssignmentOperation assign
			&& assign.Value == outer
			&& assign.Target is IParameterReferenceOperation { Parameter.RefKind: RefKind.Out };
	}

	private static bool isInside(ISymbol? containing, ITypeSymbol type) {
		for (INamedTypeSymbol? t = containing as INamedTypeSymbol ?? containing?.ContainingType; t is not null; t = t.ContainingType)
			if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, type.OriginalDefinition))
				return true;
		return false;
	}

	// ==========================================================================
	// optional parameters
	private void analyzeMethod(SymbolAnalysisContext ctx, INamedTypeSymbol iface) {
		var method = (IMethodSymbol)ctx.Symbol;
		// accessors repeat their property's parameters, and overrides repeat the base declaration
		if (method.AssociatedSymbol is not null || method.IsOverride)
			return;
		checkParameters(method.Parameters, iface, ctx.ReportDiagnostic);
	}

	private void analyzeProperty(SymbolAnalysisContext ctx, INamedTypeSymbol iface) {
		var prop = (IPropertySymbol)ctx.Symbol;
		if (prop.IsIndexer && !prop.IsOverride)
			checkParameters(prop.Parameters, iface, ctx.ReportDiagnostic);
	}

	private void analyzeLocalFunction(OperationAnalysisContext ctx, INamedTypeSymbol iface) {
		IMethodSymbol? method = ctx.Operation switch {
			ILocalFunctionOperation local => local.Symbol,
			IAnonymousFunctionOperation lambda => lambda.Symbol,
			_ => null,
		};
		if (method is not null)
			checkParameters(method.Parameters, iface, ctx.ReportDiagnostic);
	}

	private void checkParameters(ImmutableArray<IParameterSymbol> parameters, INamedTypeSymbol iface, Action<Diagnostic> report) {
		foreach (IParameterSymbol p in parameters) {
			// `= default` and `= new()` are both represented as an explicit default value of null
			if (!p.HasExplicitDefaultValue || p.ExplicitDefaultValue is not null || !hasInvalidDefault(p.Type, iface))
				continue;
			Location? loc = p.Locations.FirstOrDefault(static l => l.IsInSource);
			if (loc is not null)
				report(Diagnostic.Create(optionalParameter, loc, p.Name, display(p.Type)));
		}
	}

	// ==========================================================================
	// structs containing fields with invalid default values
	private void analyzeStruct(SymbolAnalysisContext ctx, INamedTypeSymbol iface) {
		var type = (INamedTypeSymbol)ctx.Symbol;
		if (type.TypeKind != TypeKind.Struct || type.AllInterfaces.Contains(iface, SymbolEqualityComparer.Default))
			return;
		foreach (ISymbol member in type.GetMembers()) {
			if (member is not IFieldSymbol { IsStatic: false, IsConst: false } f || !hasInvalidDefault(f.Type, iface))
				continue;
			// auto-property backing fields should get the diagnostic on the property
			ISymbol named = f.AssociatedSymbol ?? f;
			Location? loc = named.Locations.FirstOrDefault(static l => l.IsInSource) ?? type.Locations.FirstOrDefault(static l => l.IsInSource);
			if (loc is null)
				continue;
			// one per struct is enough to signal the problem
			ctx.ReportDiagnostic(Diagnostic.Create(field, loc, named.Name, display(f.Type), type.Name));
			return;
		}
	}

	private static string display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
}
