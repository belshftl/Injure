// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.Analyzers.Draw;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CanvasTargetTransformAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Draw.TargetChangedWithoutTransform
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				KnownTypes known = new(ctx.Compilation);
				if (known.CanvasParamsOverride is not INamedTypeSymbol ov || known.Canvas is not INamedTypeSymbol canvas)
					return;
				ctx.RegisterOperationAction(c => analyzeCreation(c, ov), OperationKind.ObjectCreation);
				ctx.RegisterOperationAction(c => analyzeInvocation(c, canvas), OperationKind.Invocation);
			}
		);
	}

	private static void analyzeCreation(OperationAnalysisContext ctx, INamedTypeSymbol ov) {
		var op = (IObjectCreationOperation)ctx.Operation;
		if (!SymbolEqualityComparer.Default.Equals(op.Type, ov))
			return;
		IOperation? target = null;
		bool transform = false;
		foreach (IArgumentOperation arg in op.Arguments) {
			if (arg.ArgumentKind != ArgumentKind.Explicit || arg.Parameter is null)
				continue;
			if (arg.Parameter.Name == "Target")
				target = arg;
			else if (arg.Parameter.Name == "Transform")
				transform = true;
		}
		if (op.Initializer is not null) {
			foreach (IOperation init in op.Initializer.Initializers) {
				if (init is not ISimpleAssignmentOperation { Target: IPropertyReferenceOperation prop } a)
					continue;
				if (prop.Property.Name == "Target")
					target = a;
				else if (prop.Property.Name == "Transform")
					transform = true;
			}
		}
		check(ctx, target, transform, "Transform");
	}

	private static void analyzeInvocation(OperationAnalysisContext ctx, INamedTypeSymbol canvas) {
		var op = (IInvocationOperation)ctx.Operation;
		IMethodSymbol m = op.TargetMethod;
		if (m.Name != "PushParams" || !SymbolEqualityComparer.Default.Equals(m.ContainingType, canvas))
			return;
		IOperation? target = null;
		bool transform = false;
		foreach (IArgumentOperation arg in op.Arguments) {
			if (arg.ArgumentKind != ArgumentKind.Explicit || arg.Parameter is null)
				continue;
			if (arg.Parameter.Name == "target")
				target = arg;
			else if (arg.Parameter.Name == "transform")
				transform = true;
		}
		check(ctx, target, transform, "transform");
	}

	private static void check(OperationAnalysisContext ctx, IOperation? target, bool transform, string transformName) {
		if (target is null || transform || isNull(target))
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Draw.TargetChangedWithoutTransform, target.Syntax.GetLocation(), transformName));
	}

	private static bool isNull(IOperation op) {
		IOperation value = op switch {
			IArgumentOperation a => a.Value,
			ISimpleAssignmentOperation a => a.Value,
			_ => op,
		};
		while (value is IConversionOperation c)
			value = c.Operand;
		// `default` typed as CanvasTarget? is null, but default(CanvasTarget) is an actual (invalid) target
		if (value is IDefaultValueOperation)
			return value.Type?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
		return value.ConstantValue is { HasValue: true, Value: null };
	}
}
