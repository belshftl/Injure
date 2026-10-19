// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.AnalyzerShared.Usage;

// mirrors Injure.CodeAnalysis.InterfaceImplKind
internal enum InterfaceImplKindMirror {
	Class = 1,
	Struct = 2,
}

// args:
//   - violation: {0} = interface name, {1} = what can implement the interface, either "classes" or "structs"
public abstract class InterfaceImplKindAnalyzerBase(DiagnosticDescriptor violation) : DiagnosticAnalyzer {
	private const string attributeName = "Injure.CodeAnalysis.InterfaceImplKindConstraintAttribute";

	public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(violation);

	public sealed override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(ctx => {
			if (ctx.Compilation.GetTypeByMetadataName(attributeName) is not INamedTypeSymbol attr)
				return;
			ctx.RegisterSymbolStartAction(c => analyzeNamedType(c, attr), SymbolKind.NamedType);
		});
	}

	private void analyzeNamedType(SymbolStartAnalysisContext ctx, INamedTypeSymbol attr) {
		var type = (INamedTypeSymbol)ctx.Symbol;
		InterfaceImplKindMirror actual;
		switch (type.TypeKind) {
		case TypeKind.Class:
			actual = InterfaceImplKindMirror.Class;
			break;
		case TypeKind.Struct:
			actual = InterfaceImplKindMirror.Struct;
			break;
		default:
			return;
		}

		Dictionary<INamedTypeSymbol, InterfaceImplKindMirror> violations = new(SymbolEqualityComparer.Default);
		foreach (INamedTypeSymbol iface in InterfaceImplSites.Collect(type))
			if (requiredKind(iface, attr) is InterfaceImplKindMirror required && required != actual)
				violations.Add(iface, required);
		if (violations.Count == 0)
			return;

		InterfaceImplSites.Register(ctx, type, violations.Keys, (c, iface, loc) =>
			c.ReportDiagnostic(Diagnostic.Create(
				violation,
				loc,
				iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
				violations[iface] == InterfaceImplKindMirror.Class ? "classes" : "structs"
			))
		);
	}

	// null if the interface has no constraint, or one with an undeclared kind
	private static InterfaceImplKindMirror? requiredKind(INamedTypeSymbol iface, INamedTypeSymbol attr) {
		foreach (AttributeData a in iface.OriginalDefinition.GetAttributes()) {
			if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr))
				continue;
			if (a.ConstructorArguments is [{ Value: int kind }] && Enum.IsDefined(typeof(InterfaceImplKindMirror), kind))
				return (InterfaceImplKindMirror)kind;
			return null;
		}
		return null;
	}
}
