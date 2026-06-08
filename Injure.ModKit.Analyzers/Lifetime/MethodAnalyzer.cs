// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class MethodAnalyzer(KnownTypes known, LifetimeRuleSet rules, BoundedTokenProvenance tokenProvenance) {
	private enum CatchMatch {
		None,
		Maybe,
		Definite,
	}

	private readonly KnownTypes known = known;
	private readonly LifetimeRuleSet rules = rules;
	private readonly BoundedTokenProvenance tokenProvenance = tokenProvenance;
	private readonly List<ObligationTransitionEvent> events = new();
	private readonly List<AsyncTokenWarning> asyncTokenWarnings = new();
	private readonly HashSet<int> reportedExceptionLeaks = new();
	private HashSet<SyntaxNode> processedObligationCreationSyntax = new(ReferenceEqualityComparer<SyntaxNode>.Instance);
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
			AsyncTokenWarnings = asyncTokenWarnings.ToImmutableArray(),
		};
	}

	private FlowResult analyzeStatement(IOperation op, FlowState state) {
		if (bailoutLocation is not null)
			return FlowResult.Continue(state);
		return op switch {
			IBlockOperation block => analyzeBlock(block, state),
			IConditionalOperation conditional => analyzeConditional(conditional, state),
			ILoopOperation loop => analyzeLoop(loop, state),
			IReturnOperation @return => analyzeReturn(@return, state),
			IThrowOperation @throw => analyzeThrow(@throw, state),
			IUsingOperation @using => analyzeUsing(@using, state),
			IUsingDeclarationOperation usingDecl => analyzeUsingDeclaration(usingDecl, state),
			ITryOperation @try => analyzeTry(@try, state),
			IBranchOperation branch => analyzeBranch(branch, state),
			ILabeledOperation labeled => analyzeLabeled(labeled, state),
			_ => analyzeSimpleStatement(op, state),
		};
	}

	private FlowResult analyzeBlock(IBlockOperation block, FlowState state) {
		FlowState? current = state;
		var aggregate = FlowResult.Continue(state.Clone());
		aggregate.Exits.Clear();
		foreach (IOperation stmt in block.Operations) {
			if (stmt is ILabeledOperation labeled)
				current = mergePendingGotosInto(labeled.Label, current, labeled.Syntax.GetLocation());
			if (current is null)
				continue;
			FlowResult stmtResult = analyzeStatement(stmt, current);
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

	private FlowResult analyzeSimpleStatement(IOperation op, FlowState state) {
		FlowResult result = maybeThrow(op, state, op.Syntax.GetLocation());
		if (result.ContinueState is not null)
			processEffects(op, result.ContinueState);
		return result;
	}

	private FlowResult analyzeConditional(IConditionalOperation op, FlowState state) {
		FlowResult cond = maybeThrow(op.Condition, state, op.Condition.Syntax.GetLocation());
		if (cond.ContinueState is null)
			return cond;
		FlowResult whenTrue = analyzeStatement(op.WhenTrue, cond.ContinueState.Clone());
		FlowResult whenFalse = op.WhenFalse is not null
			? analyzeStatement(op.WhenFalse, cond.ContinueState.Clone())
			: FlowResult.Continue(cond.ContinueState.Clone());
		var result = FlowResult.Merge(whenTrue, whenFalse, op.Syntax.GetLocation(), FlowMergeKind.Conditional);
		result.Exits.AddRange(cond.Exits);
		return result;
	}

	private FlowResult analyzeLoop(ILoopOperation op, FlowState state) {
		// for now approximate do as one iteration and every other loop as either zero or one iterations
		if (op is IWhileLoopOperation { ConditionIsTop: false }) {
			FlowResult body = analyzeStatement(op.Body, state.Clone());
			return oneLoopIter(body, op.Syntax.GetLocation());
		}

		FlowResult header = maybeThrow(op, state.Clone(), op.Syntax.GetLocation());
		if (header.ContinueState is null)
			return header;

		FlowState zeroState = header.ContinueState.Clone();
		FlowResult one = analyzeStatement(op.Body, header.ContinueState.Clone());
		FlowResult finishedOne = oneLoopIter(one, op.Syntax.GetLocation());
		var zero = FlowResult.Continue(zeroState);
		var result = FlowResult.Merge(zero, finishedOne, op.Syntax.GetLocation(), FlowMergeKind.LoopIteration);
		result.Exits.AddRange(header.Exits);
		return result;
	}

	private FlowResult oneLoopIter(FlowResult body, Location location) {
		FlowState? afterLoop = body.ContinueState?.Clone();
		List<ControlFlowExit> exits = new();

		foreach (ControlFlowExit exit in body.Exits) {
			if (exit.Kind is ControlExitKind.Break or ControlExitKind.Continue) {
				afterLoop = afterLoop is null ? exit.State.Clone() : FlowState.MergeWorst(afterLoop, exit.State, location, FlowMergeKind.LoopIteration);
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

	private FlowResult analyzeReturn(IReturnOperation op, FlowState state) {
		FlowResult result = op.ReturnedValue is not null ? maybeThrow(op.ReturnedValue, state, op.ReturnedValue.Syntax.GetLocation()) : FlowResult.Continue(state);

		if (result.ContinueState is not null)
			result.Exits.Add(
				new ControlFlowExit {
					Kind = ControlExitKind.Return,
					State = result.ContinueState.Clone(),
					Location = op.Syntax.GetLocation(),
					Cause = ObligationTransitionCause.Return,
				}
			);

		var final = FlowResult.NoContinue();
		final.Exits.AddRange(result.Exits);
		return final;
	}

	private FlowResult analyzeThrow(IThrowOperation op, FlowState state) {
		FlowResult result = op.Exception is not null ? maybeThrow(op.Exception, state, op.Exception.Syntax.GetLocation()) : FlowResult.Continue(state);

		if (result.ContinueState is not null)
			result.Exits.Add(
				new ControlFlowExit {
					Kind = ControlExitKind.Throw,
					State = result.ContinueState.Clone(),
					Location = op.Syntax.GetLocation(),
					Cause = ObligationTransitionCause.Throw,
					ExceptionType = op.Exception?.Type,
					ExceptionTypeUnknown = op.Exception?.Type is null,
				}
			);

		var final = FlowResult.NoContinue();
		final.Exits.AddRange(result.Exits);
		return final;
	}

	private FlowResult analyzeTry(ITryOperation op, FlowState state) {
		FlowResult result = op.Catches.Length == 0 ? analyzeStatement(op.Body, state.Clone()) : analyzeTryCatchNoFinally(op, state);
		if (op.Finally is not null)
			result = applyFinally(result, op.Finally, op.Syntax.GetLocation());
		return result;
	}

	private FlowResult analyzeTryCatchNoFinally(ITryOperation op, FlowState state) {
		FlowResult tryResult = analyzeStatement(op.Body, state.Clone());
		var result = FlowResult.NoContinue();

		if (tryResult.ContinueState is not null)
			result = FlowResult.Merge(result, FlowResult.Continue(tryResult.ContinueState.Clone()), op.Syntax.GetLocation(), FlowMergeKind.TryCatch);

		foreach (ControlFlowExit exit in tryResult.Exits) {
			if (exit.Kind == ControlExitKind.Throw) {
				result = routeThrowThroughCatches(op, exit, result);
				continue;
			}
			result.Exits.Add(exit.Clone());
		}

		return result;
	}

	private FlowResult routeThrowThroughCatches(ITryOperation op, ControlFlowExit throwExit, FlowResult result) {
		bool certainlyCaught = false;
		foreach (ICatchClauseOperation catchClause in op.Catches) {
			CatchMatch match = classifyCatch(throwExit, catchClause);
			if (match == CatchMatch.None)
				continue;

			FlowResult catchResult = analyzeStatement(catchClause.Handler, throwExit.State.Clone());
			result = FlowResult.Merge(result, catchResult, catchClause.Syntax.GetLocation(), FlowMergeKind.TryCatch);

			if (match == CatchMatch.Definite) {
				certainlyCaught = true;
				break;
			}
		}

		if (!certainlyCaught)
			result.Exits.Add(throwExit.Clone());
		return result;
	}

	private FlowResult applyFinally(FlowResult beforeFinally, IBlockOperation finallyBlock, Location location) {
		var result = FlowResult.NoContinue();

		if (beforeFinally.ContinueState is not null) {
			FlowResult afterFinally = analyzeStatement(finallyBlock, beforeFinally.ContinueState.Clone());
			result = FlowResult.Merge(result, afterFinally, location, FlowMergeKind.TryFinally);
		}

		foreach (ControlFlowExit originalExit in beforeFinally.Exits) {
			FlowResult afterFinally = analyzeStatement(finallyBlock, originalExit.State.Clone());
			foreach (ControlFlowExit finallyExit in afterFinally.Exits)
				result.Exits.Add(finallyExit.Clone());
			if (afterFinally.ContinueState is not null)
				result.Exits.Add(
					new ControlFlowExit {
						Kind = originalExit.Kind,
						State = afterFinally.ContinueState.Clone(),
						Location = originalExit.Location,
						Target = originalExit.Target,
						Cause = originalExit.Cause,
						ExceptionType = originalExit.ExceptionType,
						ExceptionTypeUnknown = originalExit.ExceptionTypeUnknown,
					}
				);
		}

		return result;
	}

	private CatchMatch classifyCatch(ControlFlowExit throwExit, ICatchClauseOperation catchClause) {
		if (catchClause.ExceptionType is null)
			return catchClause.Filter is null ? CatchMatch.Definite : CatchMatch.Maybe;
		if (isCatchAllExceptionType(catchClause.ExceptionType))
			return catchClause.Filter is null ? CatchMatch.Definite : CatchMatch.Maybe;
		if (throwExit.ExceptionTypeUnknown || throwExit.ExceptionType is null)
			return CatchMatch.Maybe;
		if (!isExceptionAssignableToCatch(throwExit.ExceptionType, catchClause.ExceptionType))
			return CatchMatch.None;
		return catchClause.Filter is null ? CatchMatch.Definite : CatchMatch.Maybe;
	}

	private bool isCatchAllExceptionType(ITypeSymbol type) =>
		known.Exception is not null && SymbolEqualityComparer.Default.Equals(type, known.Exception);

	private static bool isExceptionAssignableToCatch(ITypeSymbol thrown, ITypeSymbol caught) {
		if (SymbolEqualityComparer.Default.Equals(thrown, caught))
			return true;
		if (thrown is not INamedTypeSymbol current)
			return false;
		for (INamedTypeSymbol? baseType = current.BaseType; baseType is not null; baseType = baseType.BaseType)
			if (SymbolEqualityComparer.Default.Equals(baseType, caught))
				return true;
		return false;
	}

	private FlowResult analyzeUsing(IUsingOperation op, FlowState state) {
		FlowResult resourceResult = op.Resources is not null
			? maybeThrow(op.Resources, state.Clone(), op.Resources.Syntax.GetLocation())
			: FlowResult.Continue(state.Clone());

		if (resourceResult.ContinueState is not null && op.Resources is not null)
			processEffects(op.Resources, resourceResult.ContinueState);

		List<ILocalSymbol> usingLocals = new();
		if (op.Resources is not null)
			collectUsingLocals(op.Resources, usingLocals);

		FlowResult result = op.Body is not null && resourceResult.ContinueState is not null
			? analyzeStatement(op.Body, resourceResult.ContinueState.Clone())
			: resourceResult;

		if (result.ContinueState is not null)
			satisfyUsingLocals(result.ContinueState, usingLocals, op.Syntax.GetLocation());

		for (int i = 0; i < result.Exits.Count; i++) {
			ControlFlowExit exit = result.Exits[i];
			satisfyUsingLocals(exit.State, usingLocals, op.Syntax.GetLocation());
		}

		return result;
	}

	private FlowResult analyzeUsingDeclaration(IUsingDeclarationOperation op, FlowState state) {
		IVariableDeclarationGroupOperation decl = op.DeclarationGroup;
		FlowResult result = maybeThrow(decl, state.Clone(), decl.Syntax.GetLocation());

		if (result.ContinueState is null)
			return result;

		processEffects(decl, result.ContinueState);

		List<ILocalSymbol> usingLocals = new();
		collectUsingLocals(decl, usingLocals);
		satisfyUsingLocals(result.ContinueState, usingLocals, op.Syntax.GetLocation());

		return result;
	}

	private FlowResult analyzeBranch(IBranchOperation op, FlowState state) {
		ControlExitKind? kind = op.BranchKind switch {
			BranchKind.GoTo => ControlExitKind.Goto,
			BranchKind.Break => ControlExitKind.Break,
			BranchKind.Continue => ControlExitKind.Continue,
			_ => null,
		};

		if (kind is null) {
			bail(op.Syntax.GetLocation(), "unsupported branch operation");
			return FlowResult.Continue(state);
		}

		if (kind == ControlExitKind.Goto && op.Target is null) {
			bail(op.Syntax.GetLocation(), "goto without a normal label target isn't supported yet");
			return FlowResult.Continue(state);
		}

		return FlowResult.Stop(
			new ControlFlowExit {
				Kind = kind.Value,
				State = state.Clone(),
				Location = op.Syntax.GetLocation(),
				Target = op.Target,
				Cause = kind == ControlExitKind.Goto ? ObligationTransitionCause.UnsupportedControlFlow : ObligationTransitionCause.UnsupportedBranch,
			}
		);
	}

	private FlowResult analyzeLabeled(ILabeledOperation op, FlowState state) {
		FlowState curr = mergePendingGotosInto(op.Label, state, op.Syntax.GetLocation()) ?? state;
		if (op.Operation is null)
			return FlowResult.Continue(curr);
		return analyzeStatement(op.Operation, curr);
	}

	private FlowResult maybeThrow(IOperation op, FlowState state, Location location) {
		var result = FlowResult.Continue(state);
		if (!MayThrowClassifier.MayThrow(op))
			return result;
		if (isKnownCleanupInvocation(op, state))
			return result;
		result.Exits.Add(
			new ControlFlowExit {
				Kind = ControlExitKind.Throw,
				State = state.Clone(),
				Location = location,
				Cause = ObligationTransitionCause.MayThrow,
				ExceptionTypeUnknown = true,
			}
		);
		return result;
	}

	private bool isKnownCleanupInvocation(IOperation op, FlowState state) {
		if (op is IExpressionStatementOperation expr)
			op = expr.Operation;
		if (op is not IInvocationOperation inv)
			return false;
		foreach (LifetimeObligation obl in state.Obligations) {
			if (rules.TryGetSatisfaction(inv, obl) is not null)
				return true;
			if (rules.TryGetTransfer(inv, obl) is not null)
				return true;
		}
		return false;
	}

	private FlowState? mergePendingGotosInto(ILabelSymbol label, FlowState? curr, Location location) {
		if (!pendingGotosByTarget.TryGetValue(label, out List<PendingGoto>? jumps))
			return curr;
		FlowState? result = curr;
		foreach (PendingGoto jump in jumps)
			result = result is null ? jump.State.Clone() : FlowState.MergeWorst(result, jump.State, location, FlowMergeKind.Goto);
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
			bail(
				exit.Location,
				"backwards goto is unsupported as it can reopen satisfied obligations and make arbitrary regions execute multiple times; it's recommended to avoid backwards goto, but if you must use it then suppress this warning"
			);
			return;
		}

		if (!pendingGotosByTarget.TryGetValue(exit.Target, out List<PendingGoto>? jumps)) {
			jumps = new List<PendingGoto>();
			pendingGotosByTarget.Add(exit.Target, jumps);
		}

		jumps.Add(
			new PendingGoto {
				Target = exit.Target,
				State = exit.State.Clone(),
				Location = exit.Location,
			}
		);
	}

	private void addNonGotoExits(List<ControlFlowExit> target, List<ControlFlowExit> source) {
		foreach (ControlFlowExit exit in source)
			if (exit.Kind != ControlExitKind.Goto)
				target.Add(exit.Clone());
	}

	private void finalizeMethodExits(List<ControlFlowExit> exits) {
		foreach (ControlFlowExit exit in exits)
			switch (exit.Kind) {
			case ControlExitKind.Return:
				exit.State.TransitionOpenToLeaked(exit.Location, ObligationTransitionCause.Return, events);
				break;
			case ControlExitKind.Throw when exit.Cause == ObligationTransitionCause.MayThrow:
				List<ObligationTransitionEvent> exEvents = new();
				exit.State.TransitionOpenToExceptionLeaked(exit.Location, ObligationTransitionCause.MayThrow, exEvents);
				events.AddRange(exEvents.Where(e => reportedExceptionLeaks.Add(e.ObligationID)));
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

	private void processEffects(IOperation op, FlowState state) {
		HashSet<SyntaxNode> prevProcessedObligationCreationSyntax = processedObligationCreationSyntax;
		processedObligationCreationSyntax = new HashSet<SyntaxNode>(ReferenceEqualityComparer<SyntaxNode>.Instance);
		try {
			switch (op) {
			case IExpressionStatementOperation exprStmt:
				processExpressionStatement(exprStmt, state);
				return;
			case IVariableDeclarationGroupOperation declGroup:
				processNestedEffects(declGroup, state);
				return;
			default:
				processNestedEffects(op, state);
				return;
			}
		} finally {
			processedObligationCreationSyntax = prevProcessedObligationCreationSyntax;
		}
	}

	private void processExpressionStatement(IExpressionStatementOperation stmt, FlowState state) {
		IOperation expr = stmt.Operation;
		if (tryUnwrapObjectCreation(expr, out IObjectCreationOperation creat)) {
			processCreation(creat, local: null, state, reportDiscardedValue: true);
			processNestedEffectsSkipRoot(expr, state);
		} else if (tryUnwrapInvocation(expr, out IInvocationOperation inv)) {
			processReturnInvocationCreation(inv, local: null, state, reportDiscardedValue: true);
			processInvocation(inv, state);
			processNestedEffectsSkipRoot(expr, state);
		} else if (expr is ISimpleAssignmentOperation assignment) {
			processAssignment(assignment, state, allowDiscardCreation: true);
			processNestedEffectsSkipRoot(assignment, state);
		} else {
			processNestedEffects(expr, state);
		}
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
		case IVariableDeclaratorOperation decl:
			processVariableDeclarator(decl, state);
			break;
		case ISimpleAssignmentOperation asg:
			processAssignment(asg, state, allowDiscardCreation: false);
			break;
		case IInvocationOperation inv:
			processInvocation(inv, state);
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

	private void processVariableDeclarator(IVariableDeclaratorOperation declr, FlowState state) {
		if (declr.Initializer is null)
			return;
		tokenProvenance.ObserveAssignment(declr.Symbol, declr.Initializer.Value);
		if (tryUnwrapObjectCreation(declr.Initializer.Value, out IObjectCreationOperation creat)) {
			processCreation(creat, declr.Symbol, state, reportDiscardedValue: false);
		} else if (tryUnwrapInvocation(declr.Initializer.Value, out IInvocationOperation inv)) {
			if (tryProcessReturnedSatisfiedAssignment(inv, declr.Symbol, state))
				return;
			processReturnInvocationCreation(inv, declr.Symbol, state, reportDiscardedValue: false);
		}
	}

	private void processAssignment(ISimpleAssignmentOperation assignment, FlowState state, bool allowDiscardCreation) {
		if (LifetimeRuleSet.TryGetLocalReference(assignment.Target, out ILocalSymbol? targetLocal))
			tokenProvenance.ObserveAssignment(targetLocal, assignment.Value);

		if (tryUnwrapObjectCreation(assignment.Value, out IObjectCreationOperation creat)) {
			if (LifetimeRuleSet.TryGetLocalReference(assignment.Target, out ILocalSymbol? local)) {
				processCreation(creat, local, state, reportDiscardedValue: false);
				return;
			}
			if (allowDiscardCreation && assignment.Target is IDiscardOperation) {
				processCreation(creat, local: null, state, reportDiscardedValue: true);
				return;
			}
		}
		if (tryUnwrapInvocation(assignment.Value, out IInvocationOperation inv)) {
			if (LifetimeRuleSet.TryGetLocalReference(assignment.Target, out ILocalSymbol? local)) {
				if (tryProcessReturnedSatisfiedAssignment(inv, local, state))
					return;
				processReturnInvocationCreation(inv, local, state, reportDiscardedValue: false);
				return;
			}
			if (allowDiscardCreation && assignment.Target is IDiscardOperation) {
				processReturnInvocationCreation(inv, local: null, state, reportDiscardedValue: true);
				return;
			}
		}

		if (LifetimeRuleSet.TryGetLocalReference(assignment.Value, out ILocalSymbol? escapedLocal) &&
			state.TryGetByLocal(escapedLocal, out _))
			state.TransitionOpenToLeaked(assignment.Syntax.GetLocation(), ObligationTransitionCause.Escape, events);
	}

	private void processInvocation(IInvocationOperation inv, FlowState state) {
		processInlineTransferCreations(inv, state);

		ObligationCreation? sideEffCreation = rules.TryCreateFromSideEffectInvocation(inv);
		if (sideEffCreation is ObligationCreation created) {
			LifetimeObligation taskObl = new(
				nextObligationID++,
				created.Kind,
				created.RequiredSatisfaction,
				created.InitialSatisfaction,
				null,
				created.TypeName,
				inv.Syntax.GetLocation()
			);
			state.Add(taskObl);
			if (created.InitialSatisfaction == ObligationSatisfactionLevel.None && taskObl.TryLeak(inv.Syntax.GetLocation()))
				events.Add(ObligationTransitionEvent.From(taskObl, ObligationState.Leaked, ObligationTransitionCause.DiscardedValue, inv.Syntax.GetLocation()));
		}

		foreach (LifetimeObligation obl in state.Obligations) {
			if (obl.Local is null)
				continue;
			ObligationSatisfaction? sat = rules.TryGetSatisfaction(inv, obl);
			if (sat is not null) {
				obl.TrySatisfy(sat.Value.Level, inv.Syntax.GetLocation());
				continue;
			}
			ObligationSatisfaction? transfer = rules.TryGetTransfer(inv, obl);
			if (transfer is not null)
				obl.TrySatisfy(transfer.Value.Level, inv.Syntax.GetLocation());
		}

		for (int i = 0; i < inv.Arguments.Length; i++) {
			IArgumentOperation arg = inv.Arguments[i];
			if (!LifetimeRuleSet.TryGetLocalReference(arg.Value, out ILocalSymbol? local))
				continue;
			if (!state.TryGetByLocal(local, out LifetimeObligation obl))
				continue;
			obl.PassedToCalls.Add(new PassedToCallFact(inv.TargetMethod, i, arg.Parameter?.RefKind ?? RefKind.None, arg.Syntax.GetLocation()));
		}

		checkAsyncCancellationToken(inv);
	}

	private void processInlineTransferCreations(IInvocationOperation inv, FlowState state) {
		foreach (ParameterSatisfaction sat in rules.GetParameterSatisfactions(inv)) {
			foreach (IArgumentOperation arg in inv.Arguments) {
				if (arg.Parameter?.Ordinal != sat.ParameterOrdinal)
					continue;
				processInlineTransferValue(arg.Value, state, inv.Syntax.GetLocation(), sat.Level);
			}
		}

		ReturnedSatisfiedObligation? returned = rules.TryGetReturnedSatisfiedObligation(inv);
		if (returned is null)
			return;
		foreach (IArgumentOperation arg in inv.Arguments) {
			if (arg.Parameter?.Ordinal != returned.Value.ParameterOrdinal)
				continue;
			processInlineTransferValue(arg.Value, state, inv.Syntax.GetLocation(), returned.Value.Level);
		}
	}

	private void processInlineTransferValue(IOperation op, FlowState state, Location transferLocation, ObligationSatisfactionLevel level) {
		op = unwrapConversion(op);

		if (LifetimeRuleSet.TryGetLocalReference(op, out ILocalSymbol? local) && state.TryGetByLocal(local, out LifetimeObligation localObligation)) {
			localObligation.TrySatisfy(level, transferLocation);
			return;
		}

		switch (op) {
		case IObjectCreationOperation creat:
			processTransferredCreation(creat, state, transferLocation, level);
			break;
		case IInvocationOperation inv:
			processTransferredReturnInvocationCreation(inv, state, transferLocation, level);
			break;
		case IConditionalOperation cond:
			processInlineTransferValue(cond.WhenTrue, state, transferLocation, level);
			if (cond.WhenFalse is not null)
				processInlineTransferValue(cond.WhenFalse, state, transferLocation, level);
			break;
		case ICoalesceOperation coalesce:
			processInlineTransferValue(coalesce.Value, state, transferLocation, level);
			processInlineTransferValue(coalesce.WhenNull, state, transferLocation, level);
			break;
		case IArrayCreationOperation { Initializer: not null } arrayCreation:
			foreach (IOperation value in arrayCreation.Initializer.ElementValues)
				processInlineTransferValue(value, state, transferLocation, level);
			break;
		}
	}

	private void processTransferredCreation(IObjectCreationOperation creat, FlowState state, Location transferLocation, ObligationSatisfactionLevel level) {
		ObligationCreation? classified = rules.TryCreateFromObjectCreation(creat);
		if (classified is null || !markObligationCreationProcessed(creat.Syntax))
			return;

		ObligationCreation c = classified.Value;
		LifetimeObligation obl = new(nextObligationID++, c.Kind, c.RequiredSatisfaction, c.InitialSatisfaction, null, c.TypeName, creat.Syntax.GetLocation());
		state.Add(obl);
		obl.TrySatisfy(level, transferLocation);
	}

	private void processTransferredReturnInvocationCreation(IInvocationOperation inv, FlowState state, Location transferLocation, ObligationSatisfactionLevel level) {
		if (rules.TryGetReturnedSatisfiedObligation(inv) is not null) {
			processInlineTransferCreations(inv, state);
			return;
		}

		ObligationCreation? classified = rules.TryCreateFromReturnInvocationCreation(inv);
		if (classified is null || !markObligationCreationProcessed(inv.Syntax))
			return;

		ObligationCreation c = classified.Value;
		LifetimeObligation obl = new(nextObligationID++, c.Kind, c.RequiredSatisfaction, c.InitialSatisfaction, null, c.TypeName, inv.Syntax.GetLocation());
		state.Add(obl);
		obl.TrySatisfy(level, transferLocation);
	}

	private void processCreation(IObjectCreationOperation creat, ILocalSymbol? local, FlowState state, bool reportDiscardedValue) {
		ObligationCreation? classified = rules.TryCreateFromObjectCreation(creat);
		if (classified is null || !markObligationCreationProcessed(creat.Syntax))
			return;

		ObligationCreation c = classified.Value;
		LifetimeObligation obl = new(nextObligationID++, c.Kind, c.RequiredSatisfaction, c.InitialSatisfaction, local, c.TypeName, creat.Syntax.GetLocation());
		state.Add(obl);

		if (local is null && reportDiscardedValue && obl.TryLeak(creat.Syntax.GetLocation()))
			events.Add(ObligationTransitionEvent.From(obl, ObligationState.Leaked, ObligationTransitionCause.DiscardedValue, creat.Syntax.GetLocation()));
	}

	private void processReturnInvocationCreation(IInvocationOperation inv, ILocalSymbol? local, FlowState state, bool reportDiscardedValue) {
		if (rules.TryGetReturnedSatisfiedObligation(inv) is not null)
			return;
		ObligationCreation? classified = rules.TryCreateFromReturnInvocationCreation(inv);
		if (classified is null || !markObligationCreationProcessed(inv.Syntax))
			return;

		ObligationCreation c = classified.Value;
		LifetimeObligation obl = new(nextObligationID++, c.Kind, c.RequiredSatisfaction, c.InitialSatisfaction, local, c.TypeName, inv.Syntax.GetLocation());
		state.Add(obl);

		if (local is null && reportDiscardedValue && obl.TryLeak(inv.Syntax.GetLocation()))
			events.Add(ObligationTransitionEvent.From(obl, ObligationState.Leaked, ObligationTransitionCause.DiscardedValue, inv.Syntax.GetLocation()));
	}

	private bool tryProcessReturnedSatisfiedAssignment(IInvocationOperation inv, ILocalSymbol local, FlowState state) {
		ReturnedSatisfiedObligation? returned = rules.TryGetReturnedSatisfiedObligation(inv);
		if (returned is null)
			return false;

		foreach (IArgumentOperation arg in inv.Arguments) {
			if (arg.Parameter?.Ordinal != returned.Value.ParameterOrdinal)
				continue;
			processReturnedSatisfiedAssignmentValue(arg.Value, local, state, inv.Syntax.GetLocation(), returned.Value.Level);
			return true;
		}

		return true;
	}

	private void processReturnedSatisfiedAssignmentValue(IOperation op, ILocalSymbol local, FlowState state, Location transferLocation, ObligationSatisfactionLevel level) {
		op = unwrapConversion(op);

		if (LifetimeRuleSet.TryGetLocalReference(op, out ILocalSymbol? argumentLocal) && state.TryGetByLocal(argumentLocal, out LifetimeObligation localObligation)) {
			localObligation.TrySatisfy(level, transferLocation);
			return;
		}

		if (op is IObjectCreationOperation creat) {
			processReturnedSatisfiedCreation(creat, local, state, transferLocation, level);
			return;
		}

		if (op is IInvocationOperation inv) {
			if (rules.TryGetReturnedSatisfiedObligation(inv) is not null) {
				tryProcessReturnedSatisfiedAssignment(inv, local, state);
				return;
			}
			processReturnedSatisfiedReturnInvocationCreation(inv, local, state, transferLocation, level);
		}
	}

	private void processReturnedSatisfiedCreation(IObjectCreationOperation creat, ILocalSymbol local, FlowState state, Location transferLocation, ObligationSatisfactionLevel level) {
		ObligationCreation? classified = rules.TryCreateFromObjectCreation(creat);
		if (classified is null || !markObligationCreationProcessed(creat.Syntax))
			return;

		ObligationCreation c = classified.Value;
		LifetimeObligation obl = new(nextObligationID++, c.Kind, c.RequiredSatisfaction, c.InitialSatisfaction, local, c.TypeName, creat.Syntax.GetLocation());
		state.Add(obl);
		obl.TrySatisfy(level, transferLocation);
	}

	private void processReturnedSatisfiedReturnInvocationCreation(
		IInvocationOperation inv,
		ILocalSymbol local,
		FlowState state,
		Location transferLocation,
		ObligationSatisfactionLevel level
	) {
		ObligationCreation? classified = rules.TryCreateFromReturnInvocationCreation(inv);
		if (classified is null || !markObligationCreationProcessed(inv.Syntax))
			return;

		ObligationCreation c = classified.Value;
		LifetimeObligation obl = new(nextObligationID++, c.Kind, c.RequiredSatisfaction, c.InitialSatisfaction, local, c.TypeName, inv.Syntax.GetLocation());
		state.Add(obl);
		obl.TrySatisfy(level, transferLocation);
	}

	private bool markObligationCreationProcessed(SyntaxNode syntax) =>
		processedObligationCreationSyntax.Add(syntax);

	private static IOperation unwrapConversion(IOperation op) {
		while (op is IConversionOperation conv)
			op = conv.Operand;
		return op;
	}

	private static bool tryUnwrapObjectCreation(IOperation op, out IObjectCreationOperation creat) {
		op = unwrapConversion(op);
		if (op is IObjectCreationOperation cr) {
			creat = cr;
			return true;
		}
		creat = null!;
		return false;
	}

	private static bool tryUnwrapInvocation(IOperation op, out IInvocationOperation inv) {
		op = unwrapConversion(op);
		if (op is IInvocationOperation i) {
			inv = i;
			return true;
		}
		inv = null!;
		return false;
	}

	private static void collectUsingLocals(IOperation op, List<ILocalSymbol> locals) {
		foreach (IOperation current in enumerateUsingResourceOperations(op))
			if (current is IVariableDeclaratorOperation declr)
				locals.Add(declr.Symbol);
	}

	private static IEnumerable<IOperation> enumerateUsingResourceOperations(IOperation root) {
		yield return root;
		foreach (IOperation child in root.ChildOperations)
			foreach (IOperation nested in enumerateUsingResourceOperations(child))
				yield return nested;
	}

	private void checkAsyncCancellationToken(IInvocationOperation inv) {
		if (!isAsyncLike(inv.TargetMethod))
			return;

		bool foundTokenParam = false;
		bool hasBoundedToken = false;
		foreach (IParameterSymbol param in inv.TargetMethod.Parameters) {
			if (!isCancellationToken(param.Type))
				continue;
			foundTokenParam = true;
			break;
		}
		if (!foundTokenParam)
			return;

		foreach (IArgumentOperation arg in inv.Arguments) {
			if (arg.Parameter is null || !isCancellationToken(arg.Parameter.Type))
				continue;
			if (tokenProvenance.IsBoundedToken(arg.Value)) {
				hasBoundedToken = true;
				break;
			}
		}
		if (!hasBoundedToken)
			asyncTokenWarnings.Add(new AsyncTokenWarning(inv.TargetMethod, inv.Syntax.GetLocation()));
	}

	private static void satisfyUsingLocals(FlowState state, List<ILocalSymbol> locals, Location location) {
		foreach (ILocalSymbol local in locals)
			state.TrySatisfyLocal(local, ObligationSatisfactionLevel.Method, location);
	}

	private bool isAsyncLike(IMethodSymbol method) {
		if (method.ReturnType is not INamedTypeSymbol named)
			return false;
		return symbolEquals(named.OriginalDefinition, known.Task) ||
			symbolEquals(named.OriginalDefinition, known.TaskOfT) ||
			symbolEquals(named.OriginalDefinition, known.ValueTask) ||
			symbolEquals(named.OriginalDefinition, known.ValueTaskOfT);
	}

	private bool isCancellationToken(ITypeSymbol? type) =>
		type is INamedTypeSymbol named && known.CancellationToken is not null &&
		SymbolEqualityComparer.Default.Equals(named, known.CancellationToken);

	private static bool symbolEquals(ISymbol? left, ISymbol? right) =>
		left is not null && right is not null && SymbolEqualityComparer.Default.Equals(left, right);

	private ImmutableArray<LifetimeObligation> snapshotResult(FlowResult result) {
		FlowState? merged = result.ContinueState?.Clone();
		foreach (ControlFlowExit exit in result.Exits)
			merged = merged is null ? exit.State.Clone() : FlowState.MergeWorst(merged, exit.State, exit.Location, FlowMergeKind.Snapshot);
		return merged?.Snapshot() ?? ImmutableArray<LifetimeObligation>.Empty;
	}

	private void bail(Location location, string reason) {
		if (bailoutLocation is not null)
			return;
		bailoutLocation = location;
		bailoutReason = reason;
	}
}
