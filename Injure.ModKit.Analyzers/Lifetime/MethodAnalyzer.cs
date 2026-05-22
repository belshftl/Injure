// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class MethodAnalyzer(LifetimeRuleSet rules, GenerationTokenProvenance tokenProvenance) {
	private readonly LifetimeRuleSet rules = rules;
	private readonly GenerationTokenProvenance tokenProvenance = tokenProvenance;
	private readonly List<ObligationTransitionEvent> events = new();
	private readonly Dictionary<ILabelSymbol, List<PendingGoto>> pendingGotosByTarget = new(SymbolEqualityComparer.Default);
	private int nextObligationID;
	private LabelPositionMap? labelPositions;
	private Location? bailoutLocation;
	private string? bailoutReason;

	public MethodLifetimeResult Analyze(IOperation body) {
		labelPositions = LabelPositionMap.Build(body);
		FlowState initialState = new();
		FlowResult result = analyzeStatement(body, initialState);

		registerGotoExits(result.Exits);
		finalizeMethodExits(result.Exits);

		result.ContinueState?.TransitionOpenToLeaked(body.Syntax.GetLocation(), ObligationTransitionCause.MethodEnd, events);

		if (bailoutLocation is null && pendingGotosByTarget.Count != 0) {
			bailoutLocation = body.Syntax.GetLocation();
			bailoutReason = "method contains unresolved forward goto target(s)";
		}

		return new MethodLifetimeResult {
			AnalysisBailedOut = bailoutLocation is not null,
			BailoutLocation = bailoutLocation,
			BailoutReason = bailoutReason,
			Obligations = snapshotResult(result),
			Events = events.ToImmutableArray(),
		};
	}

	private FlowResult analyzeStatement(IOperation operation, FlowState state) {
		if (bailoutLocation is not null)
			return FlowResult.Continue(state);
		return operation switch {
			IBlockOperation block => analyzeBlock(block, state),
			IConditionalOperation conditional => analyzeConditional(conditional, state),
			ILoopOperation loop => analyzeLoop(loop, state),
			IReturnOperation @return => analyzeReturn(@return, state),
			IThrowOperation @throw => analyzeThrow(@throw, state),
			ITryOperation @try when @try.Finally is not null => analyzeTryFinally(@try, state),
			IUsingOperation @using => analyzeUsing(@using, state),
			IUsingDeclarationOperation usingDeclaration => analyzeUsingDeclaration(usingDeclaration, state),
			ITryOperation @try => analyzeTry(@try, state),
			IBranchOperation branch => analyzeBranch(branch, state),
			ILabeledOperation labeled => analyzeLabeled(labeled, state),
			_ => analyzeSimpleStatement(operation, state),
		};
	}

	private FlowResult analyzeBlock(IBlockOperation block, FlowState state) {
		FlowState? current = state;
		FlowResult aggregate = FlowResult.Continue(state.Clone());
		aggregate.Exits.Clear();
		foreach (IOperation statement in block.Operations) {
			if (statement is ILabeledOperation labeled)
				current = mergePendingGotosInto(labeled.Label, current, labeled.Syntax.GetLocation());
			if (current is null)
				continue;
			FlowResult stmtResult = analyzeStatement(statement, current);
			registerGotoExits(stmtResult.Exits);
			addNonGotoExits(aggregate.Exits, stmtResult.Exits);
			current = stmtResult.ContinueState;
		}
		FlowResult result = new() {
			ContinueState = current,
		};
		result.Exits.AddRange(aggregate.Exits);
		return result;
	}

	private FlowResult analyzeSimpleStatement(IOperation operation, FlowState state) {
		FlowResult result = maybeThrow(operation, state, operation.Syntax.GetLocation());
		if (result.ContinueState is not null)
			processEffects(operation, result.ContinueState);
		return result;
	}

	private FlowResult analyzeConditional(IConditionalOperation operation, FlowState state) {
		FlowResult cond = maybeThrow(operation.Condition, state, operation.Condition.Syntax.GetLocation());
		if (cond.ContinueState is null)
			return cond;
		FlowResult whenTrue = analyzeStatement(operation.WhenTrue, cond.ContinueState.Clone());
		FlowResult whenFalse = operation.WhenFalse is not null
			? analyzeStatement(operation.WhenFalse, cond.ContinueState.Clone())
			: FlowResult.Continue(cond.ContinueState.Clone());
		FlowResult result = FlowResult.Merge(whenTrue, whenFalse, operation.Syntax.GetLocation());
		result.Exits.AddRange(cond.Exits);
		return result;
	}

	private FlowResult analyzeLoop(ILoopOperation operation, FlowState state) {
		// for now approximate do as one iteration and every other loop as either zero or one iterations
		if (operation is IWhileLoopOperation { ConditionIsTop: false }) {
			FlowResult body = analyzeStatement(operation.Body, state.Clone());
			return oneLoopIter(body, operation.Syntax.GetLocation());
		}

		FlowResult header = maybeThrow(operation, state.Clone(), operation.Syntax.GetLocation());
		if (header.ContinueState is null)
			return header;

		FlowState zeroState = header.ContinueState.Clone();
		FlowResult one = analyzeStatement(operation.Body, header.ContinueState.Clone());
		FlowResult finishedOne = oneLoopIter(one, operation.Syntax.GetLocation());
		FlowResult zero = FlowResult.Continue(zeroState);
		FlowResult result = FlowResult.Merge(zero, finishedOne, operation.Syntax.GetLocation());
		result.Exits.AddRange(header.Exits);
		return result;
	}

	private FlowResult oneLoopIter(FlowResult body, Location location) {
		FlowState? afterLoop = body.ContinueState?.Clone();
		List<ControlFlowExit> exits = new();

		foreach (ControlFlowExit exit in body.Exits) {
			if (exit.Kind is ControlExitKind.Break or ControlExitKind.Continue) {
				afterLoop = afterLoop is null ? exit.State.Clone() : FlowState.MergeWorst(afterLoop, exit.State, location);
				continue;
			}
			exits.Add(exit.Clone());
		}

		FlowResult result = new() {
			ContinueState = afterLoop,
		};
		result.Exits.AddRange(exits);
		return result;
	}

	private FlowResult analyzeReturn(IReturnOperation operation, FlowState state) {
		FlowResult result = operation.ReturnedValue is not null
			? maybeThrow(operation.ReturnedValue, state, operation.ReturnedValue.Syntax.GetLocation())
			: FlowResult.Continue(state);

		if (result.ContinueState is not null) {
			result.Exits.Add(new ControlFlowExit {
				Kind = ControlExitKind.Return,
				State = result.ContinueState.Clone(),
				Location = operation.Syntax.GetLocation(),
				Cause = ObligationTransitionCause.Return,
			});
		}

		FlowResult final = FlowResult.NoContinue();
		final.Exits.AddRange(result.Exits);
		return final;
	}

	private FlowResult analyzeThrow(IThrowOperation operation, FlowState state) {
		FlowResult result = operation.Exception is not null
			? maybeThrow(operation.Exception, state, operation.Exception.Syntax.GetLocation())
			: FlowResult.Continue(state);

		if (result.ContinueState is not null) {
			result.Exits.Add(new ControlFlowExit {
				Kind = ControlExitKind.Throw,
				State = result.ContinueState.Clone(),
				Location = operation.Syntax.GetLocation(),
				Cause = ObligationTransitionCause.Throw,
			});
		}

		FlowResult final = FlowResult.NoContinue();
		final.Exits.AddRange(result.Exits);
		return final;
	}

	private FlowResult analyzeTryFinally(ITryOperation operation, FlowState state) {
		if (operation.Catches.Length != 0) {
			bail(operation.Syntax.GetLocation(), "support for try-catch-finally is not implemented yet");
			return FlowResult.Continue(state);
		}

		FlowResult tryResult = analyzeStatement(operation.Body, state.Clone());
		FlowResult result = FlowResult.NoContinue();

		if (tryResult.ContinueState is not null) {
			FlowResult afterFinally = analyzeStatement(operation.Finally!, tryResult.ContinueState.Clone());
			result = FlowResult.Merge(result, afterFinally, operation.Syntax.GetLocation());
		}

		foreach (ControlFlowExit originalExit in tryResult.Exits) {
			FlowResult afterFinally = analyzeStatement(operation.Finally!, originalExit.State.Clone());
			foreach (ControlFlowExit finallyExit in afterFinally.Exits)
				result.Exits.Add(finallyExit.Clone());
			if (afterFinally.ContinueState is not null) {
				result.Exits.Add(new ControlFlowExit {
					Kind = originalExit.Kind,
					State = afterFinally.ContinueState.Clone(),
					Location = originalExit.Location,
					Target = originalExit.Target,
					Cause = originalExit.Cause,
				});
			}
		}

		return result;
	}

	private FlowResult analyzeTry(ITryOperation operation, FlowState state) {
		FlowResult result = analyzeStatement(operation.Body, state.Clone());
		foreach (ICatchClauseOperation catchClause in operation.Catches) {
			FlowResult catchResult = analyzeStatement(catchClause.Handler, state.Clone());
			result = FlowResult.Merge(result, catchResult, catchClause.Syntax.GetLocation());
		}
		return result;
	}

	private FlowResult analyzeUsing(IUsingOperation operation, FlowState state) {
		FlowResult resourceResult = operation.Resources is not null
			? maybeThrow(operation.Resources, state.Clone(), operation.Resources.Syntax.GetLocation())
			: FlowResult.Continue(state.Clone());

		if (resourceResult.ContinueState is not null && operation.Resources is not null)
			processEffects(operation.Resources, resourceResult.ContinueState);

		List<ILocalSymbol> usingLocals = new();
		if (operation.Resources is not null)
			collectUsingLocals(operation.Resources, usingLocals);

		FlowResult result = operation.Body is not null && resourceResult.ContinueState is not null
			? analyzeStatement(operation.Body, resourceResult.ContinueState.Clone())
			: resourceResult;

		if (result.ContinueState is not null)
			satisfyUsingLocals(result.ContinueState, usingLocals, operation.Syntax.GetLocation());

		for (int i = 0; i < result.Exits.Count; i++) {
			ControlFlowExit exit = result.Exits[i];
			satisfyUsingLocals(exit.State, usingLocals, operation.Syntax.GetLocation());
		}

		return result;
	}

	private FlowResult analyzeUsingDeclaration(IUsingDeclarationOperation operation, FlowState state) {
		IVariableDeclarationGroupOperation declaration = operation.DeclarationGroup;
		FlowResult result = maybeThrow(declaration, state.Clone(), declaration.Syntax.GetLocation());

		if (result.ContinueState is null)
			return result;

		processEffects(declaration, result.ContinueState);

		List<ILocalSymbol> usingLocals = new();
		collectUsingLocals(declaration, usingLocals);
		satisfyUsingLocals(result.ContinueState, usingLocals, operation.Syntax.GetLocation());

		return result;
	}

	private FlowResult analyzeBranch(IBranchOperation operation, FlowState state) {
		ControlExitKind? kind = operation.BranchKind switch {
			BranchKind.GoTo => ControlExitKind.Goto,
			BranchKind.Break => ControlExitKind.Break,
			BranchKind.Continue => ControlExitKind.Continue,
			_ => null,
		};

		if (kind is null) {
			bail(operation.Syntax.GetLocation(), "unsupported branch operation");
			return FlowResult.Continue(state);
		}

		if (kind == ControlExitKind.Goto && operation.Target is null) {
			bail(operation.Syntax.GetLocation(), "goto without a normal label target isn't supported yet");
			return FlowResult.Continue(state);
		}

		return FlowResult.Stop(new ControlFlowExit {
			Kind = kind.Value,
			State = state.Clone(),
			Location = operation.Syntax.GetLocation(),
			Target = operation.Target,
			Cause = kind == ControlExitKind.Goto ? ObligationTransitionCause.UnsupportedControlFlow : ObligationTransitionCause.UnsupportedBranch,
		});
	}

	private FlowResult analyzeLabeled(ILabeledOperation operation, FlowState state) {
		FlowState curr = mergePendingGotosInto(operation.Label, state, operation.Syntax.GetLocation()) ?? state;
		if (operation.Operation is null)
			return FlowResult.Continue(curr);
		return analyzeStatement(operation.Operation, curr);
	}

	private FlowResult maybeThrow(IOperation operation, FlowState state, Location location) {
		FlowResult result = FlowResult.Continue(state);
		if (!MayThrowClassifier.MayThrow(operation))
			return result;
		if (isKnownCleanupInvocation(operation, state))
			return result;
		result.Exits.Add(new ControlFlowExit {
			Kind = ControlExitKind.Throw,
			State = state.Clone(),
			Location = location,
			Cause = ObligationTransitionCause.MayThrow,
		});
		return result;
	}

	private bool isKnownCleanupInvocation(IOperation operation, FlowState state) {
		if (operation is IExpressionStatementOperation expression)
			operation = expression.Operation;
		if (operation is not IInvocationOperation invocation)
			return false;
		foreach (LifetimeObligation obl in state.Obligations) {
			if (rules.TryGetSatisfaction(invocation, obl) is not null)
				return true;
			if (rules.TryGetTransfer(invocation, obl) is not null)
				return true;
		}
		return false;
	}

	private FlowState? mergePendingGotosInto(ILabelSymbol label, FlowState? curr, Location location) {
		if (!pendingGotosByTarget.TryGetValue(label, out List<PendingGoto>? jumps))
			return curr;
		FlowState? result = curr;
		foreach (PendingGoto jump in jumps)
			result = result is null ? jump.State.Clone() : FlowState.MergeWorst(result, jump.State, location);
		pendingGotosByTarget.Remove(label);
		return result;
	}

	private void registerGotoExits(List<ControlFlowExit> exits) {
		for (int i = exits.Count - 1; i >= 0; i--) {
			ControlFlowExit exit = exits[i];
			if (exit.Kind != ControlExitKind.Goto)
				continue;

			exits.RemoveAt(i);
			registerGoto(exit);
		}
	}

	private void registerGoto(ControlFlowExit exit) {
		if (exit.Target is null) {
			bail(exit.Location, "goto without target isn't supported yet");
			return;
		}

		if ((labelPositions?.TryGetPosition(exit.Target, out int targetPosition) ?? false) && targetPosition <= exit.Location.SourceSpan.Start) {
			bail(exit.Location, "backwards goto is unsupported as it can reopen satisfied obligations and make arbitrary regions execute multiple times; it's recommended to avoid backwards goto, but if you must use it then suppress this warning");
			return;
		}

		if (!pendingGotosByTarget.TryGetValue(exit.Target, out List<PendingGoto>? jumps)) {
			jumps = new List<PendingGoto>();
			pendingGotosByTarget.Add(exit.Target, jumps);
		}

		jumps.Add(new PendingGoto {
			Target = exit.Target,
			State = exit.State.Clone(),
			Location = exit.Location,
		});
	}

	private void addNonGotoExits(List<ControlFlowExit> target, List<ControlFlowExit> source) {
		foreach (ControlFlowExit exit in source)
			if (exit.Kind != ControlExitKind.Goto)
				target.Add(exit.Clone());
	}

	private void finalizeMethodExits(List<ControlFlowExit> exits) {
		foreach (ControlFlowExit exit in exits) {
			switch (exit.Kind) {
			case ControlExitKind.Return:
				exit.State.TransitionOpenToLeaked(exit.Location, ObligationTransitionCause.Return, events);
				break;
			case ControlExitKind.Throw when exit.Cause == ObligationTransitionCause.MayThrow:
				exit.State.TransitionOpenToExceptionLeaked(exit.Location, ObligationTransitionCause.MayThrow, events);
				break;
			case ControlExitKind.Throw:
				exit.State.TransitionOpenToLeaked(exit.Location, ObligationTransitionCause.Throw, events);
				break;
			case ControlExitKind.Break:
			case ControlExitKind.Continue:
				bail(exit.Location, "unresolved break/continue reached method boundary");
				break;
			case ControlExitKind.Goto:
				registerGoto(exit);
				break;
			}
		}
	}

	private void processEffects(IOperation operation, FlowState state) {
		switch (operation) {
		case IExpressionStatementOperation expressionStatement:
			processExpressionStatement(expressionStatement, state);
			return;
		case IVariableDeclarationGroupOperation declarationGroup:
			processNestedEffects(declarationGroup, state);
			return;
		default:
			processNestedEffects(operation, state);
			return;
		}
	}

	private void processExpressionStatement(IExpressionStatementOperation statement, FlowState state) {
		IOperation expression = statement.Operation;

		if (tryUnwrapObjectCreation(expression, out IObjectCreationOperation directCreation)) {
			processCreation(directCreation, local: null, state, reportDiscardedValue: true);
			processNestedEffectsSkipRoot(expression, state);
			return;
		}

		if (expression is ISimpleAssignmentOperation assignment) {
			processAssignment(assignment, state, allowDiscardCreation: true);
			processNestedEffectsSkipRoot(assignment, state);
			return;
		}

		processNestedEffects(expression, state);
	}

	private void processNestedEffects(IOperation root, FlowState state) {
		foreach (IOperation current in enumerateEffectOperations(root, includeRoot: true))
			processNestedEffect(current, state);
	}

	private void processNestedEffectsSkipRoot(IOperation root, FlowState state) {
		foreach (IOperation current in enumerateEffectOperations(root, includeRoot: false))
			processNestedEffect(current, state);
	}

	private void processNestedEffect(IOperation current, FlowState state) {
		switch (current) {
		case IVariableDeclaratorOperation declarator:
			processVariableDeclarator(declarator, state);
			break;
		case ISimpleAssignmentOperation assignment:
			processAssignment(assignment, state, allowDiscardCreation: false);
			break;
		case IInvocationOperation invocation:
			processInvocation(invocation, state);
			break;
		}
	}

	private IEnumerable<IOperation> enumerateEffectOperations(IOperation root, bool includeRoot) {
		if (root is IAnonymousFunctionOperation || root is ILocalFunctionOperation)
			yield break;
		if (includeRoot)
			yield return root;
		foreach (IOperation child in root.ChildOperations)
			foreach (IOperation nested in enumerateEffectOperations(child, includeRoot: true))
				yield return nested;
	}

	private void processVariableDeclarator(IVariableDeclaratorOperation declarator, FlowState state) {
		if (declarator.Initializer is null)
			return;
		tokenProvenance.ObserveAssignment(declarator.Symbol, declarator.Initializer.Value);
		if (!tryUnwrapObjectCreation(declarator.Initializer.Value, out IObjectCreationOperation creation))
			return;
		processCreation(creation, declarator.Symbol, state, reportDiscardedValue: false);
	}

	private void processAssignment(ISimpleAssignmentOperation assignment, FlowState state, bool allowDiscardCreation) {
		if (LifetimeRuleSet.TryGetLocalReference(assignment.Target, out ILocalSymbol? targetLocal))
			tokenProvenance.ObserveAssignment(targetLocal, assignment.Value);

		if (tryUnwrapObjectCreation(assignment.Value, out IObjectCreationOperation creation)) {
			if (LifetimeRuleSet.TryGetLocalReference(assignment.Target, out ILocalSymbol? local)) {
				processCreation(creation, local, state, reportDiscardedValue: false);
				return;
			}

			if (allowDiscardCreation && isDiscardTarget(assignment.Target)) {
				processCreation(creation, local: null, state, reportDiscardedValue: true);
				return;
			}
		}

		if (LifetimeRuleSet.TryGetLocalReference(assignment.Value, out ILocalSymbol? escapedLocal) &&
			state.TryGetByLocal(escapedLocal, out _)) {
			state.TransitionOpenToLeaked(assignment.Syntax.GetLocation(), ObligationTransitionCause.Escape, events);
		}
	}

	private void processInvocation(IInvocationOperation invocation, FlowState state) {
		ObligationCreation? invocationCreation = rules.TryCreateFromInvocation(invocation);
		if (invocationCreation is ObligationCreation created) {
			LifetimeObligation taskObligation = new(
				nextObligationID++,
				created.Kind,
				created.RequiredSatisfaction,
				created.InitialSatisfaction,
				null,
				created.DisplayName,
				invocation.Syntax.GetLocation()
			);
			state.Add(taskObligation);
			if (created.InitialSatisfaction == ObligationSatisfactionLevel.None && taskObligation.TryLeak(invocation.Syntax.GetLocation()))
				events.Add(ObligationTransitionEvent.From(taskObligation, ObligationState.Leaked, ObligationTransitionCause.DiscardedValue, invocation.Syntax.GetLocation()));
		}

		foreach (LifetimeObligation obl in state.Obligations) {
			if (obl.Local is null)
				continue;

			ObligationSatisfaction? satisfaction = rules.TryGetSatisfaction(invocation, obl);
			if (satisfaction is not null) {
				state.TrySatisfyLocal(obl.Local, satisfaction.Value.Level, invocation.Syntax.GetLocation());
				continue;
			}

			ObligationSatisfaction? transfer = rules.TryGetTransfer(invocation, obl);
			if (transfer is not null)
				state.TrySatisfyLocal(obl.Local, transfer.Value.Level, invocation.Syntax.GetLocation());
		}

		for (int i = 0; i < invocation.Arguments.Length; i++) {
			IArgumentOperation arg = invocation.Arguments[i];
			if (!LifetimeRuleSet.TryGetLocalReference(arg.Value, out ILocalSymbol? local))
				continue;
			if (!state.TryGetByLocal(local, out LifetimeObligation obl))
				continue;
			obl.PassedToCalls.Add(new PassedToCallFact(invocation.TargetMethod, i, arg.Parameter?.RefKind ?? RefKind.None, arg.Syntax.GetLocation()));
		}
	}

	private void processCreation(IObjectCreationOperation creation, ILocalSymbol? local, FlowState state, bool reportDiscardedValue) {
		ObligationCreation? classified = rules.TryCreateFromObjectCreation(creation);
		if (classified is null)
			return;

		ObligationCreation c = classified.Value;
		LifetimeObligation obl = new(
			nextObligationID++,
			c.Kind,
			c.RequiredSatisfaction,
			c.InitialSatisfaction,
			local,
			local?.Name ?? c.DisplayName,
			creation.Syntax.GetLocation()
		);

		state.Add(obl);

		if (local is null && reportDiscardedValue && obl.TryLeak(creation.Syntax.GetLocation()))
			events.Add(ObligationTransitionEvent.From(obl, ObligationState.Leaked, ObligationTransitionCause.DiscardedValue, creation.Syntax.GetLocation()));
	}

	private static bool tryUnwrapObjectCreation(IOperation operation, out IObjectCreationOperation creation) {
		while (operation is IConversionOperation conversion)
			operation = conversion.Operand;
		if (operation is IObjectCreationOperation objectCreation) {
			creation = objectCreation;
			return true;
		}
		creation = null!;
		return false;
	}

	private static bool isDiscardTarget(IOperation operation) {
		if (operation is IDiscardOperation)
			return true;
		return operation.Syntax.ToString() == "_";
	}

	private static void collectUsingLocals(IOperation operation, List<ILocalSymbol> locals) {
		foreach (IOperation current in enumerateUsingResourceOperations(operation))
			if (current is IVariableDeclaratorOperation declarator)
				locals.Add(declarator.Symbol);
	}

	private static IEnumerable<IOperation> enumerateUsingResourceOperations(IOperation root) {
		yield return root;
		foreach (IOperation child in root.ChildOperations)
			foreach (IOperation nested in enumerateUsingResourceOperations(child))
				yield return nested;
	}

	private static void satisfyUsingLocals(FlowState state, List<ILocalSymbol> locals, Location location) {
		foreach (ILocalSymbol local in locals)
			state.TrySatisfyLocal(local, ObligationSatisfactionLevel.Method, location);
	}

	private ImmutableArray<LifetimeObligation> snapshotResult(FlowResult result) {
		FlowState? merged = result.ContinueState?.Clone();
		foreach (ControlFlowExit exit in result.Exits)
			merged = merged is null ? exit.State.Clone() : FlowState.MergeWorst(merged, exit.State, exit.Location);
		return merged?.Snapshot() ?? ImmutableArray<LifetimeObligation>.Empty;
	}

	private void bail(Location location, string reason) {
		if (bailoutLocation is not null)
			return;
		bailoutLocation = location;
		bailoutReason = reason;
	}
}
