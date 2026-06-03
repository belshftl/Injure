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
		Diagnostics.Discouraged.EmitDelegateStaticLambda
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
		if (delegateArg is null)
			return;
		report(ctx, delegateArg, Diagnostics.Discouraged.NonStaticHookMethod, Diagnostics.Discouraged.HookStaticLambda);
	}

	private static void analyzeInvocation(OperationAnalysisContext ctx, KnownSymbols known) {
		var inv = (IInvocationOperation)ctx.Operation;
		if (!known.EmitDelegateMethods.Contains(inv.TargetMethod.OriginalDefinition, SymbolEqualityComparer.Default))
			return;
		IArgumentOperation? delegateArg = findDelegateArgument(inv.Arguments);
		if (delegateArg is null)
			return;
		report(ctx, delegateArg, Diagnostics.Discouraged.NonStaticEmitDelegateMethod, Diagnostics.Discouraged.EmitDelegateStaticLambda);
	}

	private static IArgumentOperation? findDelegateArgument(ImmutableArray<IArgumentOperation> args) {
		foreach (IArgumentOperation arg in args)
			if (arg.Value.Type?.TypeKind == TypeKind.Delegate)
				return arg;
		return null;
	}

	private static void report(OperationAnalysisContext ctx, IArgumentOperation arg, DiagnosticDescriptor nonStaticDd, DiagnosticDescriptor staticLambdaDd) {
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
}
