// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.Analyzers.Assets;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssetBorrowValueAnalyzer : DiagnosticAnalyzer {
	private static readonly HashSet<string> collectionAdders = new(StringComparer.Ordinal) {
		"Add",
		"AddFirst",
		"AddLast",
		"AddRange",
		"Insert",
		"InsertRange",
		"Push",
		"Enqueue",
		"TryAdd",
		"GetOrAdd",
		"AddOrUpdate",
		"Set",
		"SetValue",
	};

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Assets.BorrowedValueStored
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				KnownTypes known = new(ctx.Compilation);
				if (known.AssetBorrow is not INamedTypeSymbol borrow)
					return;
				ctx.RegisterOperationBlockAction(c => analyzeBlock(c, borrow));
			}
		);
	}

	private static void analyzeBlock(OperationBlockAnalysisContext ctx, INamedTypeSymbol borrow) {
		List<IOperation> all = new();
		foreach (IOperation block in ctx.OperationBlocks)
			all.AddRange(block.DescendantsAndSelf());

		// locals holding a borrowed value, to a fixed point so chains of locals are followed
		HashSet<ILocalSymbol> tainted = new(SymbolEqualityComparer.Default);
		for (bool changed = true; changed;) {
			changed = false;
			foreach (IOperation op in all) {
				(ILocalSymbol? local, IOperation? value) = op switch {
					IVariableDeclaratorOperation d => (d.Symbol, d.Initializer?.Value ?? d.GetVariableInitializer()?.Value),
					ISimpleAssignmentOperation { Target: ILocalReferenceOperation l } a => (l.Local, a.Value),
					_ => (null, null),
				};
				if (local is not null && value is not null && isBorrowed(value, borrow, tainted) && tainted.Add(local))
					changed = true;
			}
		}

		foreach (IOperation op in all) {
			switch (op) {
			case ISimpleAssignmentOperation a when isBorrowed(a.Value, borrow, tainted) && storageName(a.Target) is string where:
				report(ctx, a.Value, where);
				break;
			case IFieldInitializerOperation fi when isBorrowed(fi.Value, borrow, tainted):
				report(ctx, fi.Value, "stored in a field");
				break;
			case IPropertyInitializerOperation pi when isBorrowed(pi.Value, borrow, tainted):
				report(ctx, pi.Value, "stored in a property");
				break;
			case IInvocationOperation inv when collectionAdders.Contains(inv.TargetMethod.Name) && inv.Instance is not null:
				foreach (IArgumentOperation arg in inv.Arguments)
					if (isBorrowed(arg.Value, borrow, tainted))
						report(ctx, arg.Value, $"added to a collection with '{inv.TargetMethod.Name}'");
				break;
			case ILocalReferenceOperation lr when tainted.Contains(lr.Local) && capturedBy(lr) is string fn:
				report(ctx, lr, $"captured by {fn}");
				break;
			}
		}
	}

	private static void report(OperationBlockAnalysisContext ctx, IOperation at, string where) =>
		ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.BorrowedValueStored, at.Syntax.GetLocation(), where));

	private static string? storageName(IOperation target) => target switch {
		IFieldReferenceOperation => "stored in a field",
		IPropertyReferenceOperation p when p.Property.IsIndexer => "stored through an indexer",
		IPropertyReferenceOperation => "stored in a property",
		IArrayElementReferenceOperation => "stored in an array",
		IEventReferenceOperation => "stored in an event",
		_ => null,
	};

	private static bool isBorrowed(IOperation value, INamedTypeSymbol borrow, HashSet<ILocalSymbol> tainted) => value switch {
		IConversionOperation c => isBorrowed(c.Operand, borrow, tainted),
		IParenthesizedOperation p => isBorrowed(p.Operand, borrow, tainted),
		IConditionalOperation c => isBorrowed(c.WhenTrue, borrow, tainted)
			|| (c.WhenFalse is not null && isBorrowed(c.WhenFalse, borrow, tainted)),
		ICoalesceOperation c => isBorrowed(c.Value, borrow, tainted) || isBorrowed(c.WhenNull, borrow, tainted),
		ILocalReferenceOperation l => tainted.Contains(l.Local),
		IPropertyReferenceOperation p => p.Property.Name == "Value"
			&& SymbolEqualityComparer.Default.Equals(p.Property.ContainingType.OriginalDefinition, borrow),
		_ => false,
	};

	// the lambda or local function that captures this local reference, if the local is declared
	// outside it
	private static string? capturedBy(ILocalReferenceOperation lr) {
		for (IOperation? o = lr.Parent; o is not null; o = o.Parent) {
			IMethodSymbol? fn = o switch {
				IAnonymousFunctionOperation a => a.Symbol,
				ILocalFunctionOperation l => l.Symbol,
				_ => null,
			};
			if (fn is null)
				continue;
			if (!SymbolEqualityComparer.Default.Equals(lr.Local.ContainingSymbol, fn) && !isDeclaredWithin(lr.Local, o))
				return fn.MethodKind == MethodKind.LocalFunction ? $"local function '{fn.Name}'" : "a lambda";
			return null;
		}
		return null;
	}

	private static bool isDeclaredWithin(ILocalSymbol local, IOperation fn) =>
		fn.Descendants().OfType<IVariableDeclaratorOperation>().Any(d => SymbolEqualityComparer.Default.Equals(d.Symbol, local));
}
