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

internal sealed class LifetimeRuleSet(KnownTypes known, GenerationTokenProvenance tokenProvenance) {
	private readonly KnownTypes known = known;
	private readonly GenerationTokenProvenance tokenProvenance = tokenProvenance;

	public ObligationCreation? TryCreateFromObjectCreation(IObjectCreationOperation creation) {
		static ObligationCreation required(LifetimeObligationKind kind, string displayName) =>
			new(kind, ObligationSatisfactionLevel.Generation, ObligationSatisfactionLevel.None, displayName);
		ITypeSymbol? type = creation.Type;
		if (known.IsOrDerivesFrom(type, known.Hook))
			return required(LifetimeObligationKind.Hook, "hook");
		if (known.IsOrDerivesFrom(type, known.ILHook))
			return required(LifetimeObligationKind.ILHook, "IL hook");
		if (known.IsOrDerivesFrom(type, known.NativeDetour))
			return required(LifetimeObligationKind.NativeDetour, "native detour");
		if (known.IsOrDerivesFrom(type, known.Thread))
			return required(LifetimeObligationKind.Thread, "thread");
		if (known.IsOrDerivesFrom(type, known.Timer) || known.IsOrDerivesFrom(type, known.TimersTimer))
			return required(LifetimeObligationKind.Timer, "timer");
		if (known.IsOrDerivesFrom(type, known.PeriodicTimer))
			return required(LifetimeObligationKind.PeriodicTimer, "periodic timer");
		if (known.IsOrDerivesFrom(type, known.CancellationTokenSource))
			return required(LifetimeObligationKind.CancellationTokenSource, "cancellation token source");
		if (known.IsOrDerivesFrom(type, known.AssemblyLoadContext))
			return required(LifetimeObligationKind.AssemblyLoadContext, "assembly load context");
		if (known.IsOrDerivesFrom(type, known.Process))
			return required(LifetimeObligationKind.Process, "process");
		return null;
	}

	public ObligationCreation? TryCreateFromReturnInvocationCreation(IInvocationOperation invocation) {
		static ObligationCreation required(LifetimeObligationKind kind, string displayName) =>
			new(kind, ObligationSatisfactionLevel.Generation, ObligationSatisfactionLevel.None, displayName);
		IMethodSymbol method = invocation.TargetMethod;
		if (isAssetStoreRegistryMethod(method))
			return required(LifetimeObligationKind.AssetStoreRegistration, "asset store registration");
		return null;
	}

	public ObligationCreation? TryCreateFromSideEffectInvocation(IInvocationOperation invocation) {
		IMethodSymbol method = invocation.TargetMethod;
		if (isTaskRun(method) || isTaskFactoryStartNew(method))
			return new ObligationCreation(
				LifetimeObligationKind.StartedTask,
				ObligationSatisfactionLevel.Generation,
				HasGenerationBoundedTokenArgument(invocation) ? ObligationSatisfactionLevel.Generation : ObligationSatisfactionLevel.None,
				"started task"
			);
		if (isThreadPoolQueue(method))
			return new ObligationCreation(
				LifetimeObligationKind.ThreadPoolWorkItem,
				ObligationSatisfactionLevel.Generation,
				HasGenerationBoundedTokenArgument(invocation) ? ObligationSatisfactionLevel.Generation : ObligationSatisfactionLevel.None,
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
			LifetimeObligationKind.AssetStoreRegistration => tryDispose(invocation, obl),
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

	public bool HasGenerationBoundedTokenArgument(IInvocationOperation invocation) {
		foreach (IArgumentOperation arg in invocation.Arguments)
			if (tokenProvenance.IsGenerationBoundedToken(arg.Value))
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

	private bool isOwnerScopeTransferMethod(IMethodSymbol method) {
		if (!(method.Name is "AddDisposable" or "AddAsyncDisposable" or "AddOrderedDisposable" or "AddOrderedAsyncDisposable"))
			return false;
		INamedTypeSymbol? containing = method.ContainingType;
		if (containing is null || known.IActiveOwnerScope is null)
			return false;
		if (SymbolEqualityComparer.Default.Equals(containing.OriginalDefinition, known.IActiveOwnerScope.OriginalDefinition))
			return true;
		return known.Implements(containing, known.IActiveOwnerScope);
	}

	private bool isAssetStoreRegistryMethod(IMethodSymbol method) =>
		(method.Name is "RegisterSource" or "RegisterResolver" ||
		(method.Name is "RegisterCreator" or "RegisterDependencyWatcher" && method.Arity == 1) ||
		(method.Name is "RegisterStagedCreator" && method.Arity == 2)) && SymbolEqualityComparer.Default.Equals(method.ContainingType, known.AssetStore);

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
