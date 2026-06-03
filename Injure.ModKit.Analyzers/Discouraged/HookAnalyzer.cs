// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Discouraged;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HookAnalyzer : DiagnosticAnalyzer {
	private enum DelegateTargetKind {
		PlainStaticMethodGroup,
		StaticLambda,
		NonStaticOrUnknown,
	}

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.Discouraged.NonStaticHookMethod,
		Diagnostics.Discouraged.HookStaticLambda,
		Diagnostics.Discouraged.NonStaticEmitDelegateMethod,
		Diagnostics.Discouraged.EmitDelegateStaticLambda,
		Diagnostics.Discouraged.ManualHookWithDetourConfig,
		Diagnostics.Discouraged.ManualHookWithoutDetourConfig
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
			KnownSymbols known = new(ctx.Compilation);
			ctx.RegisterOperationAction(c => analyzeObjectCreation(c, known), OperationKind.ObjectCreation);
			ctx.RegisterOperationAction(c => analyzeInvocation(c, known), OperationKind.Invocation);
		});
	}

	private static void analyzeObjectCreation(OperationAnalysisContext ctx, KnownSymbols known) {
		var creat = (IObjectCreationOperation)ctx.Operation;
		if (!creat.Type.IsHook(known))
			return;
		IArgumentOperation? delegateArg = findDelegateArgument(creat.Arguments);
		if (delegateArg is not null)
			maybeReportDelegateArg(ctx, delegateArg, Diagnostics.Discouraged.NonStaticHookMethod, Diagnostics.Discouraged.HookStaticLambda);
		IArgumentOperation? detourConfigArg = findDetourConfigArgument(creat.Arguments, known);
		if (detourConfigArg is null || isMaybeNull(detourConfigArg.Value))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.ManualHookWithoutDetourConfig, creat.Syntax.GetLocation()));
		else
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.ManualHookWithDetourConfig, creat.Syntax.GetLocation()));
	}

	private static void analyzeInvocation(OperationAnalysisContext ctx, KnownSymbols known) {
		var inv = (IInvocationOperation)ctx.Operation;
		if (!known.EmitDelegateMethods.Contains(inv.TargetMethod.OriginalDefinition, SymbolEqualityComparer.Default))
			return;
		IArgumentOperation? delegateArg = findDelegateArgument(inv.Arguments);
		if (delegateArg is not null)
			maybeReportDelegateArg(ctx, delegateArg, Diagnostics.Discouraged.NonStaticEmitDelegateMethod, Diagnostics.Discouraged.EmitDelegateStaticLambda);
	}

	private static IArgumentOperation? findDelegateArgument(ImmutableArray<IArgumentOperation> args) {
		foreach (IArgumentOperation arg in args)
			if (arg.Value.Type?.TypeKind == TypeKind.Delegate)
				return arg;
		return null;
	}

	private static IArgumentOperation? findDetourConfigArgument(ImmutableArray<IArgumentOperation> args, KnownSymbols known) {
		foreach (IArgumentOperation arg in args) {
			IParameterSymbol? param = arg.Parameter;
			if (param is null)
				continue;
			if (SymbolEqualityComparer.Default.Equals(param.Type, known.DetourConfig))
				return arg;
		}
		return null;
	}

	private static void maybeReportDelegateArg(OperationAnalysisContext ctx, IArgumentOperation arg, DiagnosticDescriptor nonStaticDd, DiagnosticDescriptor staticLambdaDd) {
		switch (classifyDelegateExpression(arg.Value)) {
		case DelegateTargetKind.PlainStaticMethodGroup:
			return;
		case DelegateTargetKind.StaticLambda:
			ctx.ReportDiagnostic(Diagnostic.Create(staticLambdaDd, arg.Syntax.GetLocation()));
			return;
		case DelegateTargetKind.NonStaticOrUnknown:
			ctx.ReportDiagnostic(Diagnostic.Create(nonStaticDd, arg.Syntax.GetLocation()));
			return;
		}
	}

	private static DelegateTargetKind classifyDelegateExpression(IOperation op) {
		while (op is IConversionOperation conv && conv.IsImplicit)
			op = conv.Operand;
		if (op is IDelegateCreationOperation del)
			return classifyDelegateExpression(del.Target);
		if (op is IMethodReferenceOperation methodRef)
			return methodRef.Method.IsStatic ? DelegateTargetKind.PlainStaticMethodGroup : DelegateTargetKind.NonStaticOrUnknown;
		if (op is IAnonymousFunctionOperation anon)
			return anon.Symbol.IsStatic ? DelegateTargetKind.StaticLambda : DelegateTargetKind.NonStaticOrUnknown;
		if (op.Type?.TypeKind == TypeKind.Delegate)
			return DelegateTargetKind.NonStaticOrUnknown;
		return DelegateTargetKind.NonStaticOrUnknown;
	}

	private static bool isMaybeNull(IOperation value) {
		while (value is IConversionOperation conv)
			value = conv.Operand;
		if (value.ConstantValue is { HasValue: true, Value: null })
			return true;
		if (value is IDefaultValueOperation)
			return true;
		if (value is ILocalReferenceOperation localRef) {
			NullableFlowState? nullable = localRef.SemanticModel?.GetTypeInfo(localRef.Syntax).Nullability.FlowState;
			if (nullable == NullableFlowState.MaybeNull)
				return true;
		}
		if (value is IParameterReferenceOperation paramRef) {
			NullableFlowState? nullable = paramRef.SemanticModel?.GetTypeInfo(paramRef.Syntax).Nullability.FlowState;
			if (nullable == NullableFlowState.MaybeNull)
				return true;
		}
		if (value.SemanticModel is not null) {
			NullableFlowState? nullable = value.SemanticModel.GetTypeInfo(value.Syntax).Nullability.FlowState;
			if (nullable == NullableFlowState.MaybeNull)
				return true;
		}
		return false;
	}
}
