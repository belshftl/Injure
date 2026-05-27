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

	public ObligationSatisfaction? TryGetTransfer(IInvocationOperation invocation, LifetimeObligation obl) {
		if (obl.Local is null)
			return null;
		if (isOwnerScopeTransferMethod(invocation.TargetMethod))
			foreach (IArgumentOperation arg in invocation.Arguments)
				if (TryGetLocalReference(arg.Value, out ILocalSymbol? local) && SymbolEqualityComparer.Default.Equals(local, obl.Local))
					return new ObligationSatisfaction(ObligationSatisfactionLevel.Generation, "transferred to active owner scope");
		return null;
	}

	public bool HasBoundedTokenArgument(IInvocationOperation invocation) {
		foreach (IArgumentOperation arg in invocation.Arguments)
			if (tokenProvenance.IsBoundedToken(arg.Value))
				return true;
		return false;
	}

	public bool IsTransferMethod(IMethodSymbol method) => isOwnerScopeTransferMethod(method);

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

	private bool isOwnerScopeTransferMethod(IMethodSymbol method) {
		if (!(method.Name is "AddTeardown" or "AddDisposable" or "AddAsyncDisposable" or "AddOrderedDisposable" or "AddOrderedAsyncDisposable"))
			return false;
		INamedTypeSymbol? containing = method.ContainingType;
		if (containing is null || known.IActiveOwnerScope is null)
			return false;
		return containing.IsOrImplements(known.IActiveOwnerScope);
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
