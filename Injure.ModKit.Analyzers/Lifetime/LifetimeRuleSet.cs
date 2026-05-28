// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Lifetime;

internal readonly record struct ObligationCreation(
	LifetimeObligationKind Kind,
	ObligationSatisfactionLevel RequiredSatisfaction,
	ObligationSatisfactionLevel InitialSatisfaction,
	string DisplayName
);

internal readonly record struct ObligationSatisfaction(
	ObligationSatisfactionLevel Level,
	string Reason
);

internal readonly record struct ReturnedSatisfiedObligation(
	int ParameterOrdinal,
	ObligationSatisfactionLevel Level,
	string Reason
);

internal sealed class LifetimeRuleSet(KnownTypes known, BoundedTokenProvenance tokenProvenance) {
	private readonly KnownTypes known = known;
	private readonly BoundedTokenProvenance tokenProvenance = tokenProvenance;

	public ObligationCreation? TryCreateFromObjectCreation(IObjectCreationOperation creation) {
		static ObligationCreation required(LifetimeObligationKind kind, string displayName) =>
			new(kind, ObligationSatisfactionLevel.Generation, ObligationSatisfactionLevel.None, displayName);
		ITypeSymbol? type = creation.Type;
		if (type.IsOrDerivesFrom(known.Hook))
			return required(LifetimeObligationKind.Hook, "hook");
		if (type.IsOrDerivesFrom(known.ILHook))
			return required(LifetimeObligationKind.ILHook, "IL hook");
		if (type.IsOrDerivesFrom(known.NativeDetour))
			return required(LifetimeObligationKind.NativeDetour, "native detour");
		if (type.IsOrDerivesFrom(known.Thread))
			return required(LifetimeObligationKind.Thread, "thread");
		if (type.IsOrDerivesFrom(known.Timer) || type.IsOrDerivesFrom(known.TimersTimer))
			return required(LifetimeObligationKind.Timer, "timer");
		if (type.IsOrDerivesFrom(known.PeriodicTimer))
			return required(LifetimeObligationKind.PeriodicTimer, "periodic timer");
		if (type.IsOrDerivesFrom(known.CancellationTokenSource))
			return required(LifetimeObligationKind.CancellationTokenSource, "cancellation token source");
		if (type.IsOrDerivesFrom(known.AssemblyLoadContext))
			return required(LifetimeObligationKind.AssemblyLoadContext, "assembly load context");
		if (type.IsOrDerivesFrom(known.Process))
			return required(LifetimeObligationKind.Process, "process");
		return null;
	}

	public ObligationCreation? TryCreateFromReturnInvocationCreation(IInvocationOperation invocation) {
		static ObligationCreation required(LifetimeObligationKind kind, string displayName) =>
			new(kind, ObligationSatisfactionLevel.Generation, ObligationSatisfactionLevel.None, displayName);
		IMethodSymbol method = invocation.TargetMethod;
		if (method.ReturnType.IsOrDerivesFrom(known.AssetStoreRegistration))
			return required(LifetimeObligationKind.AssetStoreRegistration, "asset store registration");
		if (method.ReturnType.IsOrDerivesFrom(known.TickerHandle))
			return required(LifetimeObligationKind.Ticker, "ticker");
		if (method.ReturnType.IsOrDerivesFrom(known.TickerSubscriptionHandle))
			return required(LifetimeObligationKind.TickerSubscription, "ticker subscription");
		if (method.ReturnType.IsOrImplements(known.IReloadTeardown))
			return required(LifetimeObligationKind.IReloadTeardown, "IReloadTeardown object");
		return null;
	}

	public ObligationCreation? TryCreateFromSideEffectInvocation(IInvocationOperation invocation) {
		IMethodSymbol method = invocation.TargetMethod;
		if (isTaskRun(method) || isTaskFactoryStartNew(method))
			return new ObligationCreation(
				LifetimeObligationKind.StartedTask,
				ObligationSatisfactionLevel.Generation,
				HasBoundedTokenArgument(invocation) ? ObligationSatisfactionLevel.Generation : ObligationSatisfactionLevel.None,
				"started task"
			);
		if (isThreadPoolQueue(method))
			return new ObligationCreation(
				LifetimeObligationKind.ThreadPoolWorkItem,
				ObligationSatisfactionLevel.Generation,
				HasBoundedTokenArgument(invocation) ? ObligationSatisfactionLevel.Generation : ObligationSatisfactionLevel.None,
				"thread pool work item"
			);
		return null;
	}

	public ObligationSatisfaction? TryGetSatisfaction(IInvocationOperation invocation, LifetimeObligation obl) {
		if (obl.Local is null)
			return null;
		return obl.Kind switch {
			LifetimeObligationKind.Hook => tryDispose(invocation, obl),
			LifetimeObligationKind.ILHook => tryDispose(invocation, obl),
			LifetimeObligationKind.NativeDetour => tryDispose(invocation, obl),
			LifetimeObligationKind.Thread => tryThreadJoin(invocation, obl),
			LifetimeObligationKind.Timer => tryDispose(invocation, obl),
			LifetimeObligationKind.PeriodicTimer => tryDispose(invocation, obl),
			LifetimeObligationKind.CancellationTokenSource => tryDispose(invocation, obl),
			LifetimeObligationKind.AssemblyLoadContext => tryAlcUnload(invocation, obl),
			LifetimeObligationKind.Process => tryDispose(invocation, obl),
			LifetimeObligationKind.AssetStoreRegistration => tryNoArgsRemoveMethod(invocation, obl) ?? tryTeardown(invocation, obl),
			LifetimeObligationKind.Ticker => tryNoArgsRemoveMethod(invocation, obl) ?? tryTeardown(invocation, obl),
			LifetimeObligationKind.TickerSubscription => tryNoArgsRemoveMethod(invocation, obl) ?? tryTeardown(invocation, obl),
			LifetimeObligationKind.IReloadTeardown => tryTeardown(invocation, obl),
			_ => null,
		};
	}

	public ReturnedSatisfiedObligation? TryGetReturnedSatisfiedObligation(IInvocationOperation invocation) {
		AttributeData? attr = tryGetAttribute(invocation.TargetMethod, known.SatisfiesAndReturnsAttribute);
		if (attr is null)
			return null;

		int ordinal = -1;
		if (attr.ConstructorArguments.Length == 1) {
			TypedConstant arg = attr.ConstructorArguments[0];
			if (arg.Value is int index) {
				ordinal = index;
			} else if (arg.Value is string name) {
				for (int i = 0; i < invocation.TargetMethod.Parameters.Length; i++) {
					if (invocation.TargetMethod.Parameters[i].Name == name) {
						ordinal = i;
						break;
					}
				}
			}
		}
		if (ordinal < 0 || ordinal >= invocation.TargetMethod.Parameters.Length)
			return null;
		return new ReturnedSatisfiedObligation(ordinal, ObligationSatisfactionLevel.Generation, "returned generation-satisfied obligation");
	}

	public ObligationSatisfaction? TryGetTransfer(IInvocationOperation invocation, LifetimeObligation obl) {
		if (obl.Local is null)
			return null;
		ReturnedSatisfiedObligation? returned = TryGetReturnedSatisfiedObligation(invocation);
		if (returned is null)
			return null;
		foreach (IArgumentOperation argument in invocation.Arguments) {
			if (argument.Parameter?.Ordinal != returned.Value.ParameterOrdinal)
				continue;
			if (TryGetLocalReference(argument.Value, out ILocalSymbol? local) && SymbolEqualityComparer.Default.Equals(local, obl.Local))
				return new ObligationSatisfaction(returned.Value.Level, returned.Value.Reason);
		}
		return null;
	}

	public bool HasBoundedTokenArgument(IInvocationOperation invocation) {
		foreach (IArgumentOperation arg in invocation.Arguments)
			if (tokenProvenance.IsBoundedToken(arg.Value))
				return true;
		return false;
	}

	private ObligationSatisfaction? tryDispose(IInvocationOperation invocation, LifetimeObligation obl) {
		if (!isReceiverLocal(invocation, obl.Local!))
			return null;
		IMethodSymbol method = invocation.TargetMethod;
		if (method.IsStatic || method.IsExtensionMethod)
			return null;
		if (method.Name != "Dispose" || !method.ReturnsVoid || method.Parameters.Length != 0)
			return null;
		return new ObligationSatisfaction(ObligationSatisfactionLevel.Method, "disposed");
	}

	private ObligationSatisfaction? tryTeardown(IInvocationOperation invocation, LifetimeObligation obl) {
		if (!isReceiverLocal(invocation, obl.Local!))
			return null;
		IMethodSymbol method = invocation.TargetMethod;
		if (method.IsStatic || method.IsExtensionMethod)
			return null;
		if (method.Name != "Teardown" || !method.ReturnsVoid || method.Parameters.Length != 1)
			return null;
		return new ObligationSatisfaction(ObligationSatisfactionLevel.Method, "torn down");
	}

	private ObligationSatisfaction? tryThreadJoin(IInvocationOperation invocation, LifetimeObligation obl) {
		if (!isReceiverLocal(invocation, obl.Local!))
			return null;
		IMethodSymbol method = invocation.TargetMethod;
		if (method.IsStatic || method.IsExtensionMethod)
			return null;
		if (method.Name != "Join")
			return null;
		return new ObligationSatisfaction(ObligationSatisfactionLevel.Method, "joined");
	}

	private ObligationSatisfaction? tryAlcUnload(IInvocationOperation invocation, LifetimeObligation obl) {
		if (!isReceiverLocal(invocation, obl.Local!))
			return null;
		IMethodSymbol method = invocation.TargetMethod;
		if (method.IsStatic || method.IsExtensionMethod)
			return null;
		if (method.Name != "Unload" || !method.ReturnsVoid || method.Parameters.Length != 0)
			return null;
		return new ObligationSatisfaction(ObligationSatisfactionLevel.Method, "unloaded");
	}

	private ObligationSatisfaction? tryNoArgsRemoveMethod(IInvocationOperation invocation, LifetimeObligation obl) {
		if (!isReceiverLocal(invocation, obl.Local!))
			return null;
		IMethodSymbol method = invocation.TargetMethod;
		if (method.IsStatic || method.IsExtensionMethod)
			return null;
		if (method.Name != "Remove" || method.Parameters.Length != 0)
			return null;
		return new ObligationSatisfaction(ObligationSatisfactionLevel.Method, "removed");
	}

	private bool isTaskRun(IMethodSymbol method) =>
		method.Name == "Run" && SymbolEqualityComparer.Default.Equals(method.ContainingType, known.Task);

	private bool isTaskFactoryStartNew(IMethodSymbol method) =>
		method.Name == "StartNew" && method.ContainingType?.Name == "TaskFactory";

	private bool isThreadPoolQueue(IMethodSymbol method) =>
		method.Name is "QueueUserWorkItem" or "UnsafeQueueUserWorkItem" && SymbolEqualityComparer.Default.Equals(method.ContainingType, known.ThreadPool);

	private static bool isReceiverLocal(IInvocationOperation invocation, ILocalSymbol local) =>
		invocation.Instance is not null && TryGetLocalReference(invocation.Instance, out ILocalSymbol? receiverLocal) &&
			SymbolEqualityComparer.Default.Equals(receiverLocal, local);

	private static AttributeData? tryGetAttribute(IMethodSymbol method, INamedTypeSymbol? attrType) {
		if (attrType is null)
			return null;
		foreach (AttributeData attr in method.GetAttributes())
			if (isAttribute(attr, attrType))
				return attr;
		if (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition))
			foreach (AttributeData attr in method.OriginalDefinition.GetAttributes())
				if (isAttribute(attr, attrType))
					return attr;
		return null;
	}

	private static bool isAttribute(AttributeData attr, INamedTypeSymbol attrType) => attr.AttributeClass is not null &&
		SymbolEqualityComparer.Default.Equals(attr.AttributeClass.OriginalDefinition, attrType.OriginalDefinition);

	public static bool TryGetLocalReference(IOperation operation, out ILocalSymbol local) {
		if (operation is IConversionOperation conversion)
			return TryGetLocalReference(conversion.Operand, out local);
		if (operation is ILocalReferenceOperation localReference) {
			local = localReference.Local;
			return true;
		}
		local = null!;
		return false;
	}
}
