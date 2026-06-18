// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Injure.ModKit.Analyzers.Lifetime;

internal readonly record struct ObligationCreation(
	LifetimeObligationKind Kind,
	ObligationSatisfactionLevel RequiredSatisfaction,
	ObligationSatisfactionLevel InitialSatisfaction,
	string TypeName
);

internal readonly record struct ObligationSatisfaction(
	ObligationSatisfactionLevel Level,
	string Reason
);

internal readonly record struct ParameterSatisfaction(
	int ParameterOrdinal,
	ObligationSatisfactionLevel Level,
	string Reason
);

internal readonly record struct ReturnedSatisfiedObligation(
	int ParameterOrdinal,
	ObligationSatisfactionLevel Level,
	string Reason
);

internal readonly record struct ObjectSatisfaction(
	ObligationSatisfactionLevel Level,
	string Reason
);

internal sealed class LifetimeRuleSet(KnownTypes known, BoundedTokenProvenance tokenProvenance) {
	private readonly KnownTypes known = known;
	private readonly BoundedTokenProvenance tokenProvenance = tokenProvenance;

	public ObligationCreation? TryCreateFromObjectCreation(IObjectCreationOperation creation) {
		static ObligationCreation required(LifetimeObligationKind kind, ITypeSymbol type) =>
			new(kind, ObligationSatisfactionLevel.Generation, ObligationSatisfactionLevel.None, type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat));
		ITypeSymbol? type = creation.Type;
		if (hasAttribute(creation.Constructor, known.DoesNotCreateObligationAttribute))
			return null;
		if (type.IsOrDerivesFrom(known.Hook))
			return required(LifetimeObligationKind.Hook, type);
		if (type.IsOrDerivesFrom(known.ILHook))
			return required(LifetimeObligationKind.ILHook, type);
		if (type.IsOrDerivesFrom(known.NativeHook))
			return required(LifetimeObligationKind.NativeHook, type);
		if (type.IsOrDerivesFrom(known.Thread))
			return required(LifetimeObligationKind.Thread, type);
		if (type.IsOrDerivesFrom(known.Timer) || type.IsOrDerivesFrom(known.TimersTimer))
			return required(LifetimeObligationKind.Timer, type);
		if (type.IsOrDerivesFrom(known.PeriodicTimer))
			return required(LifetimeObligationKind.PeriodicTimer, type);
		if (type.IsOrDerivesFrom(known.CancellationTokenSource))
			return required(LifetimeObligationKind.CancellationTokenSource, type);
		if (type.IsOrDerivesFrom(known.AssemblyLoadContext))
			return required(LifetimeObligationKind.AssemblyLoadContext, type);
		if (type.IsOrDerivesFrom(known.Process))
			return required(LifetimeObligationKind.Process, type);
		if (isAttributedObligationType(type, out INamedTypeSymbol? declaringType))
			return required(LifetimeObligationKind.Attributed, declaringType);
		return null;
	}

	public ObligationCreation? TryCreateFromReturnInvocationCreation(IInvocationOperation inv) {
		IMethodSymbol method = inv.TargetMethod;
		if (hasAttribute(method, known.DoesNotCreateObligationAttribute))
			return null;
		if (isAttributedObligationType(method.ReturnType, out INamedTypeSymbol? declaringType))
			return new ObligationCreation(
				LifetimeObligationKind.Attributed,
				ObligationSatisfactionLevel.Generation,
				ObligationSatisfactionLevel.None,
				declaringType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)
			);
		return null;
	}

	public ObligationCreation? TryCreateFromSideEffectInvocation(IInvocationOperation inv) {
		IMethodSymbol method = inv.TargetMethod;
		if (hasAttribute(method, known.DoesNotCreateObligationAttribute))
			return null;
		if (isTaskRun(method) || isTaskFactoryStartNew(method))
			return new ObligationCreation(
				LifetimeObligationKind.StartedTask,
				ObligationSatisfactionLevel.Generation,
				HasBoundedTokenArgument(inv) ? ObligationSatisfactionLevel.Generation : ObligationSatisfactionLevel.None,
				method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)
			);
		if (isThreadPoolQueue(method))
			return new ObligationCreation(
				LifetimeObligationKind.ThreadPoolWorkItem,
				ObligationSatisfactionLevel.Generation,
				HasBoundedTokenArgument(inv) ? ObligationSatisfactionLevel.Generation : ObligationSatisfactionLevel.None,
				method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)
			);
		return null;
	}

	public ObligationSatisfaction? TryGetSatisfaction(IInvocationOperation inv, LifetimeObligation obl) {
		if (obl.Local is null)
			return null;
		ObligationSatisfaction? objectSatisfaction = tryGetObjectSatisfaction(inv, obl);
		if (objectSatisfaction is not null)
			return objectSatisfaction;
		return obl.Kind switch {
			LifetimeObligationKind.Hook => tryDispose(inv, obl),
			LifetimeObligationKind.ILHook => tryDispose(inv, obl),
			LifetimeObligationKind.NativeHook => tryDispose(inv, obl),
			LifetimeObligationKind.Thread => tryThreadJoin(inv, obl),
			LifetimeObligationKind.Timer => tryDispose(inv, obl),
			LifetimeObligationKind.PeriodicTimer => tryDispose(inv, obl),
			LifetimeObligationKind.CancellationTokenSource => tryDispose(inv, obl),
			LifetimeObligationKind.AssemblyLoadContext => tryAlcUnload(inv, obl),
			LifetimeObligationKind.Process => tryDispose(inv, obl),
			_ => null,
		};
	}

	public ImmutableArray<ParameterSatisfaction> GetParameterSatisfactions(IInvocationOperation inv) {
		if (known.SatisfiesObligationAttribute is null)
			return ImmutableArray<ParameterSatisfaction>.Empty;
		ImmutableArray<ParameterSatisfaction>.Builder builder = ImmutableArray.CreateBuilder<ParameterSatisfaction>();
		foreach (AttributeData attr in getMethodAttributes(inv.TargetMethod)) {
			if (!isAttribute(attr, known.SatisfiesObligationAttribute))
				continue;
			if (!tryResolveParameterOrdinal(inv.TargetMethod, attr, out int ordinal))
				continue;
			ObligationSatisfactionLevel level = readSatisfactionLevel(attr, 1);
			if (level == ObligationSatisfactionLevel.None)
				continue;
			builder.Add(new ParameterSatisfaction(ordinal, level, "satisfied by annotated method"));
		}
		return builder.ToImmutable();
	}

	public ReturnedSatisfiedObligation? TryGetReturnedSatisfiedObligation(IInvocationOperation inv) {
		AttributeData? attr = tryGetAttribute(inv.TargetMethod, known.SatisfiesAndReturnsObligationAttribute);
		if (attr is null)
			return null;
		if (!tryResolveParameterOrdinal(inv.TargetMethod, attr, out int ordinal))
			return null;
		ObligationSatisfactionLevel level = readSatisfactionLevel(attr, 1);
		if (level == ObligationSatisfactionLevel.None)
			return null;
		return new ReturnedSatisfiedObligation(ordinal, level, "satisfied and returned by annotated method");
	}

	public ObligationSatisfaction? TryGetTransfer(IInvocationOperation inv, LifetimeObligation obl) {
		if (obl.Local is null)
			return null;

		foreach (ParameterSatisfaction sat in GetParameterSatisfactions(inv)) {
			foreach (IArgumentOperation argument in inv.Arguments) {
				if (argument.Parameter?.Ordinal != sat.ParameterOrdinal)
					continue;
				if (TryGetLocalReference(argument.Value, out ILocalSymbol? local) && SymbolEqualityComparer.Default.Equals(local, obl.Local))
					return new ObligationSatisfaction(sat.Level, sat.Reason);
			}
		}

		ReturnedSatisfiedObligation? returned = TryGetReturnedSatisfiedObligation(inv);
		if (returned is null)
			return null;
		foreach (IArgumentOperation argument in inv.Arguments) {
			if (argument.Parameter?.Ordinal != returned.Value.ParameterOrdinal)
				continue;
			if (TryGetLocalReference(argument.Value, out ILocalSymbol? local) && SymbolEqualityComparer.Default.Equals(local, obl.Local))
				return new ObligationSatisfaction(returned.Value.Level, returned.Value.Reason);
		}
		return null;
	}

	public bool HasBoundedTokenArgument(IInvocationOperation inv) {
		foreach (IArgumentOperation arg in inv.Arguments)
			if (tokenProvenance.IsBoundedToken(arg.Value))
				return true;
		return false;
	}

	private ObligationSatisfaction? tryGetObjectSatisfaction(IInvocationOperation inv, LifetimeObligation obl) {
		AttributeData? attr = tryGetAttribute(inv.TargetMethod, known.SatisfiesObjectObligationAttribute);
		if (attr is null)
			return null;
		ObligationSatisfactionLevel level = readSatisfactionLevel(attr, 0);
		if (level == ObligationSatisfactionLevel.None)
			return null;
		if (!tryGetInvocationReceiver(inv, out IOperation recv))
			return null;
		if (!TryGetLocalReference(recv, out ILocalSymbol? recvLocal))
			return null;
		if (!SymbolEqualityComparer.Default.Equals(recvLocal, obl.Local))
			return null;
		return new ObligationSatisfaction(level, "satisfied by annotated object method");
	}

	private ObligationSatisfaction? tryMethod(IInvocationOperation inv, LifetimeObligation obl, Func<IMethodSymbol, bool> check, string reason) {
		if (!isReceiverLocal(inv, obl.Local!))
			return null;
		IMethodSymbol method = inv.TargetMethod;
		if (method.IsStatic || method.IsExtensionMethod)
			return null;
		if (!check(method))
			return null;
		return new ObligationSatisfaction(ObligationSatisfactionLevel.Method, reason);
	}

	private ObligationSatisfaction? tryDispose(IInvocationOperation inv, LifetimeObligation obl) =>
		tryMethod(inv, obl, static m => m.Name == "Dispose" && m.ReturnsVoid && m.Parameters.Length == 0 && m.Arity == 0, "disposed");

	private ObligationSatisfaction? tryThreadJoin(IInvocationOperation inv, LifetimeObligation obl) =>
		tryMethod(inv, obl, static m => m.Name == "Join" && m.Arity == 0, "joined");

	private ObligationSatisfaction? tryAlcUnload(IInvocationOperation inv, LifetimeObligation obl) =>
		tryMethod(inv, obl, static m => m.Name == "Unload" && m.ReturnsVoid && m.Parameters.Length == 0 && m.Arity == 0, "unloaded");

	private bool isTaskRun(IMethodSymbol method) =>
		method.Name == "Run" && SymbolEqualityComparer.Default.Equals(method.ContainingType, known.Task);

	private bool isTaskFactoryStartNew(IMethodSymbol method) =>
		method.Name == "StartNew" && method.ContainingType?.Name == "TaskFactory";

	private bool isThreadPoolQueue(IMethodSymbol method) =>
		method.Name is "QueueUserWorkItem" or "UnsafeQueueUserWorkItem" && SymbolEqualityComparer.Default.Equals(method.ContainingType, known.ThreadPool);

	private static bool isReceiverLocal(IInvocationOperation inv, ILocalSymbol local) =>
		tryGetInvocationReceiver(inv, out IOperation recv) && TryGetLocalReference(recv, out ILocalSymbol? recvLocal) && SymbolEqualityComparer.Default.Equals(recvLocal, local);

	private static bool tryGetInvocationReceiver(IInvocationOperation inv, out IOperation recv) {
		if (inv.Instance is not null) {
			recv = inv.Instance;
			return true;
		}
		if (inv.TargetMethod.IsExtensionMethod && inv.Arguments.Length != 0 && inv.Arguments[0].Parameter?.Ordinal == 0) {
			recv = inv.Arguments[0].Value;
			return true;
		}
		recv = null!;
		return false;
	}

	private static bool isAttribute(AttributeData attr, INamedTypeSymbol attrType) => attr.AttributeClass is not null &&
		SymbolEqualityComparer.Default.Equals(attr.AttributeClass.OriginalDefinition, attrType.OriginalDefinition);

	private static AttributeData? tryGetAttribute(IMethodSymbol method, INamedTypeSymbol? attrType) {
		if (attrType is null)
			return null;
		foreach (AttributeData attr in getMethodAttributes(method))
			if (isAttribute(attr, attrType))
				return attr;
		return null;
	}

	private static bool hasDirectAttribute(ISymbol? symbol, INamedTypeSymbol? attrType) {
		if (symbol is null || attrType is null)
			return false;
		foreach (AttributeData attr in symbol.GetAttributes())
			if (isAttribute(attr, attrType))
				return true;
		return false;
	}

	private static bool hasAttribute(ISymbol? symbol, INamedTypeSymbol? attrType) {
		if (symbol is null || attrType is null)
			return false;
		if (symbol is IMethodSymbol method) {
			foreach (AttributeData attr in getMethodAttributes(method))
				if (isAttribute(attr, attrType))
					return true;
			return false;
		}
		foreach (AttributeData attr in symbol.GetAttributes())
			if (isAttribute(attr, attrType))
				return true;
		if (symbol is INamedTypeSymbol type)
			for (INamedTypeSymbol? curr = type.BaseType; curr is not null; curr = curr.BaseType)
				foreach (AttributeData attr in curr.GetAttributes())
					if (isAttribute(attr, attrType))
						return true;
		return false;
	}

	private static IMethodSymbol normalizeMethod(IMethodSymbol method) {
		if (method.ReducedFrom is not null)
			method = method.ReducedFrom;
		if (method.PartialImplementationPart is not null)
			method = method.PartialImplementationPart;
		if (method.PartialDefinitionPart is not null)
			method = method.PartialDefinitionPart;
		return method;
	}

	private static bool sameMethodImplementation(IMethodSymbol left, IMethodSymbol right) {
		left = normalizeMethod(left);
		right = normalizeMethod(right);
		return SymbolEqualityComparer.Default.Equals(left, right) || SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition);
	}

	private static bool methodLooksLikeImplicitImplementation(IMethodSymbol method, IMethodSymbol ifaceMethod) {
		if (method.MethodKind != MethodKind.Ordinary)
			return false;
		if (method.IsStatic)
			return false;
		if (method.Name != ifaceMethod.Name)
			return false;
		if (method.Arity != ifaceMethod.Arity)
			return false;
		if (method.Parameters.Length != ifaceMethod.Parameters.Length)
			return false;
		for (int i = 0; i < method.Parameters.Length; i++) {
			IParameterSymbol left = method.Parameters[i];
			IParameterSymbol right = ifaceMethod.Parameters[i];
			if (left.RefKind != right.RefKind)
				return false;
			if (!SymbolEqualityComparer.Default.Equals(left.Type.OriginalDefinition, right.Type.OriginalDefinition))
				return false;
		}
		return true;
	}

	private static IEnumerable<IMethodSymbol> getImplementedInterfaceMethods(IMethodSymbol method) {
		INamedTypeSymbol? containing = method.ContainingType;
		if (containing is null)
			yield break;
		foreach (IMethodSymbol explicitImpl in method.ExplicitInterfaceImplementations)
			yield return explicitImpl;
		if (containing.TypeKind == TypeKind.Interface)
			yield break;
		foreach (INamedTypeSymbol iface in containing.AllInterfaces) {
			foreach (ISymbol member in iface.GetMembers(method.Name)) {
				if (member is not IMethodSymbol ifaceMethod)
					continue;
				ISymbol? impl = containing.FindImplementationForInterfaceMember(ifaceMethod);
				if (impl is IMethodSymbol implMethod && sameMethodImplementation(implMethod, method))
					yield return ifaceMethod;
				else if (methodLooksLikeImplicitImplementation(method, ifaceMethod)) // fallback
					yield return ifaceMethod;
			}
		}
	}

	private static ImmutableArray<AttributeData> getMethodAttributes(IMethodSymbol method) {
		ImmutableArray<AttributeData>.Builder builder = ImmutableArray.CreateBuilder<AttributeData>();
		HashSet<ISymbol> seen = new(SymbolEqualityComparer.Default);

		addMethodAttributes(method);
		if (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition))
			addMethodAttributes(method.OriginalDefinition);
		for (IMethodSymbol? overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod) {
			addMethodAttributes(overridden);
			if (!SymbolEqualityComparer.Default.Equals(overridden, overridden.OriginalDefinition))
				addMethodAttributes(overridden.OriginalDefinition);
		}
		foreach (IMethodSymbol ifaceMethod in getImplementedInterfaceMethods(method)) {
			addMethodAttributes(ifaceMethod);
			if (!SymbolEqualityComparer.Default.Equals(ifaceMethod, ifaceMethod.OriginalDefinition))
				addMethodAttributes(ifaceMethod.OriginalDefinition);
		}
		return builder.ToImmutable();

		void addMethodAttributes(IMethodSymbol source) {
			if (!seen.Add(source))
				return;
			builder.AddRange(source.GetAttributes());
		}
	}

	private bool isAttributedObligationType([NotNullWhen(true)] ITypeSymbol? type, [NotNullWhen(true)] out INamedTypeSymbol? declaringType) {
		declaringType = null;
		if (type is not INamedTypeSymbol named || named.IsValueType)
			return false;
		if (hasDirectAttribute(named, known.ObligationAttribute)) {
			declaringType = named;
			return true;
		}
		for (INamedTypeSymbol? curr = named.BaseType; curr is not null; curr = curr.BaseType)
			if (hasDirectAttribute(curr, known.ObligationAttribute)) {
				declaringType = curr;
				return true;
			}
		foreach (INamedTypeSymbol iface in named.AllInterfaces)
			if (hasDirectAttribute(iface, known.ObligationAttribute)) {
				declaringType = iface;
				return true;
			}
		return false;
	}

	private static bool tryResolveParameterOrdinal(IMethodSymbol method, AttributeData attr, out int ordinal) {
		ordinal = -1;
		if (attr.ConstructorArguments.Length < 1)
			return false;
		object? value = attr.ConstructorArguments[0].Value;
		if (value is int index) {
			if ((uint)index >= (uint)method.Parameters.Length)
				return false;
			ordinal = index;
			return true;
		}
		if (value is string name)
			for (int i = 0; i < method.Parameters.Length; i++)
				if (method.Parameters[i].Name == name) {
					ordinal = i;
					return true;
				}
		return false;
	}

	private static ObligationSatisfactionLevel readSatisfactionLevel(AttributeData attr, int argumentIndex) {
		if (attr.ConstructorArguments.Length <= argumentIndex || attr.ConstructorArguments[argumentIndex].Value is null)
			return ObligationSatisfactionLevel.None;
		return (ObligationSatisfactionLevel)(int)attr.ConstructorArguments[argumentIndex].Value!;
	}

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
