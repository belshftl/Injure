// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

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
		Diagnostics.Discouraged.ManualHookWithoutDetourConfig,
		Diagnostics.Discouraged.DestructiveILEdit,
		Diagnostics.Discouraged.PreferRequireGoto
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				KnownSymbols known = new(ctx.Compilation);
				ctx.RegisterOperationAction(c => analyzeObjectCreation(c, known), OperationKind.ObjectCreation);
				ctx.RegisterOperationAction(c => analyzeInvocation(c, known), OperationKind.Invocation);
				ctx.RegisterOperationAction(
					c => analyzeAssignment(c, known),
					OperationKind.SimpleAssignment,
					OperationKind.CompoundAssignment,
					OperationKind.Increment,
					OperationKind.Decrement
				);
			}
		);
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
		if (known.EmitDelegateMethods.Contains(inv.TargetMethod.OriginalDefinition)) {
			IArgumentOperation? delegateArg = findDelegateArgument(inv.Arguments);
			if (delegateArg is not null)
				maybeReportDelegateArg(ctx, delegateArg, Diagnostics.Discouraged.NonStaticEmitDelegateMethod, Diagnostics.Discouraged.EmitDelegateStaticLambda);
		} else if (known.RemoveInstructionMethods.Contains(inv.TargetMethod.OriginalDefinition)) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.DestructiveILEdit, inv.Syntax.GetLocation()));
		} else if (known.GotoMethods.Contains(inv.TargetMethod.OriginalDefinition)) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.PreferRequireGoto, inv.Syntax.GetLocation()));
		} else {
			ITypeSymbol? receiverType = inv.Instance?.Type;
			if (receiverType is null || !isInstructionListLike(receiverType, known))
				return;
			if (inv.TargetMethod.Name is "Add" or "AddRange" or "Clear" or "Insert" or "InsertRange" or "Remove" or "RemoveAt" or "RemoveRange")
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.DestructiveILEdit, inv.Syntax.GetLocation()));
		}
	}

	private static void analyzeAssignment(OperationAnalysisContext ctx, KnownSymbols known) {
		IOperation? left = ctx.Operation switch {
			ISimpleAssignmentOperation op => op.Target,
			ICompoundAssignmentOperation op => op.Target,
			IIncrementOrDecrementOperation op => op.Target,
			_ => null,
		};
		if (left is null)
			return;

		if (left is IPropertyReferenceOperation propRef) {
			IPropertySymbol prop = propRef.Property.OriginalDefinition;
			if (known.InstructionMembers.Contains(prop) ||
				propRef.Property.IsIndexer && propRef.Instance?.Type is {} receiverType && isInstructionListLike(receiverType, known)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.DestructiveILEdit, left.Syntax.GetLocation()));
				return;
			}
		}

		if (left is IFieldReferenceOperation fieldRef && known.InstructionMembers.Contains(fieldRef.Field.OriginalDefinition))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Discouraged.DestructiveILEdit, left.Syntax.GetLocation()));
	}

	private static bool isInstructionListLike(ITypeSymbol type, KnownSymbols known) {
		if (known.Instruction is null)
			return false;
		if (isGenericInstructionCollection(type, known.Instruction))
			return true;
		foreach (INamedTypeSymbol iface in type.AllInterfaces)
			if (isGenericInstructionCollection(iface, known.Instruction))
				return true;
		return false;
	}

	private static bool isGenericInstructionCollection(ITypeSymbol type, INamedTypeSymbol instructionType) {
		if (type is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
			return false;
		if (!SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], instructionType))
			return false;
		return named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
			"global::System.Collections.Generic.ICollection<T>" or
			"global::System.Collections.Generic.IList<T>" or
			"global::System.Collections.Generic.List<T>" or
			"global::Mono.Collections.Generic.Collection<T>";
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
