// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

using Injure.Weaver.Model;

using ResultDict = System.Collections.Generic.Dictionary<string, Injure.Weaver.Model.PublicizedStateMachineKind>;

namespace Injure.Weaver.Patching;

public static class StateMachineDiscovery {
	public static ResultDict Discover(IReadOnlyList<TypeDefinition> allTypes) {
		ResultDict result = new(StringComparer.Ordinal);

		foreach (TypeDefinition type in allTypes) {
			foreach (MethodDefinition method in type.Methods) {
				if (!method.HasBody)
					continue;
				discoverFromMethod(method, result);
			}
		}

		return result;
	}

	private static void discoverFromMethod(MethodDefinition method, ResultDict result) {
		discoverAsyncStateMachineFromBuilderStart(method, result);
		discoverIteratorStateMachineFromNewobj(method, result);
	}

	private static void discoverAsyncStateMachineFromBuilderStart(MethodDefinition method, ResultDict result) {
		if (!method.HasBody)
			return;

		foreach (Instruction instruction in method.Body.Instructions) {
			if (instruction.OpCode.Code != Code.Call && instruction.OpCode.Code != Code.Callvirt)
				continue;
			if (instruction.Operand is not GenericInstanceMethod genericCall)
				continue;

			MethodReference elementMethod = genericCall.ElementMethod;
			if (elementMethod.Name != "Start")
				continue;
			if (!isAsyncMethodBuilderType(elementMethod.DeclaringType))
				continue;
			if (genericCall.GenericArguments.Count != 1)
				continue;

			TypeDefinition? candidate = tryResolve(genericCall.GenericArguments[0]);
			if (candidate is null)
				continue;
			if (!isLikelyAsyncStateMachine(candidate, method))
				continue;

			PublicizedStateMachineKind kind = returnsAsyncEnumerableOrEnumerator(method.ReturnType)
				? PublicizedStateMachineKind.AsyncIterator
				: PublicizedStateMachineKind.Async;

			addOrUpgrade(result, candidate.FullName, kind);
		}
	}

	private static void discoverIteratorStateMachineFromNewobj(MethodDefinition method, ResultDict result) {
		if (!method.HasBody)
			return;
		if (!returnsEnumerableOrEnumerator(method.ReturnType))
			return;
		foreach (Instruction instruction in method.Body.Instructions) {
			if (instruction.OpCode.Code != Code.Newobj)
				continue;
			if (instruction.Operand is not MethodReference ctor)
				continue;

			TypeDefinition? candidate = tryResolve(ctor.DeclaringType);
			if (candidate is null)
				continue;
			if (!isLikelyIteratorStateMachine(candidate, method))
				continue;

			PublicizedStateMachineKind kind = implementsAsyncEnumerableOrEnumerator(candidate)
				? PublicizedStateMachineKind.AsyncIterator
				: PublicizedStateMachineKind.Iterator;

			addOrUpgrade(result, candidate.FullName, kind);
		}
	}

	private static bool isLikelyAsyncStateMachine(TypeDefinition candidate, MethodDefinition wrapperMethod) {
		if (!GeneratedCodePolicy.HasCompilerGeneratedAttribute(candidate))
			return false;
		if (!isNestedInSameType(candidate, wrapperMethod.DeclaringType))
			return false;
		if (!nameLooksLikeStateMachineFor(candidate, wrapperMethod))
			return false;
		if (!implementsInterface(candidate, "System.Runtime.CompilerServices.IAsyncStateMachine"))
			return false;
		if (!hasMethod(candidate, "MoveNext"))
			return false;
		if (!hasMethod(candidate, "SetStateMachine"))
			return false;
		if (!hasStateField(candidate))
			return false;
		return true;
	}

	private static bool isLikelyIteratorStateMachine(TypeDefinition candidate, MethodDefinition wrapperMethod) {
		if (!GeneratedCodePolicy.HasCompilerGeneratedAttribute(candidate))
			return false;
		if (!isNestedInSameType(candidate, wrapperMethod.DeclaringType))
			return false;
		if (!nameLooksLikeStateMachineFor(candidate, wrapperMethod))
			return false;
		if (!hasMethod(candidate, "MoveNext"))
			return false;
		if (!hasStateField(candidate))
			return false;
		if (!implementsEnumerableOrEnumerator(candidate))
			return false;
		return true;
	}

	private static bool isAsyncMethodBuilderType(TypeReference type) =>
		type.FullName == "System.Runtime.CompilerServices.AsyncTaskMethodBuilder" ||
			type.FullName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1", StringComparison.Ordinal) ||
			type.FullName == "System.Runtime.CompilerServices.AsyncVoidMethodBuilder" ||
			type.FullName == "System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder" ||
			type.FullName.StartsWith("System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder`1", StringComparison.Ordinal) ||
			type.FullName.StartsWith("System.Runtime.CompilerServices.AsyncIteratorMethodBuilder", StringComparison.Ordinal);

	private static bool returnsEnumerableOrEnumerator(TypeReference type) =>
		isEnumerableOrEnumeratorType(type) || isAsyncEnumerableOrEnumeratorType(type);

	private static bool returnsAsyncEnumerableOrEnumerator(TypeReference type) =>
		isAsyncEnumerableOrEnumeratorType(type);

	private static bool implementsEnumerableOrEnumerator(TypeDefinition type) =>
		implementsSyncEnumerableOrEnumerator(type) || implementsAsyncEnumerableOrEnumerator(type);

	private static bool implementsSyncEnumerableOrEnumerator(TypeDefinition type) {
		foreach (InterfaceImplementation ii in type.Interfaces) {
			TypeReference interfaceType = ii.InterfaceType;
			if (isEnumerableOrEnumeratorType(interfaceType))
				return true;
		}
		return false;
	}

	private static bool implementsAsyncEnumerableOrEnumerator(TypeDefinition type) {
		foreach (InterfaceImplementation ii in type.Interfaces) {
			TypeReference interfaceType = ii.InterfaceType;
			if (isAsyncEnumerableOrEnumeratorType(interfaceType))
				return true;
		}
		return false;
	}

	private static bool isEnumerableOrEnumeratorType(TypeReference type) {
		TypeReference normalized = unwrapGenericInstance(type);
		return normalized.FullName == "System.Collections.IEnumerable" ||
			normalized.FullName == "System.Collections.IEnumerator" ||
			normalized.FullName.StartsWith("System.Collections.Generic.IEnumerable`1", StringComparison.Ordinal) ||
			normalized.FullName.StartsWith("System.Collections.Generic.IEnumerator`1", StringComparison.Ordinal);
	}

	private static bool isAsyncEnumerableOrEnumeratorType(TypeReference type) {
		TypeReference normalized = unwrapGenericInstance(type);
		return normalized.FullName.StartsWith("System.Collections.Generic.IAsyncEnumerable`1", StringComparison.Ordinal) ||
			normalized.FullName.StartsWith("System.Collections.Generic.IAsyncEnumerator`1", StringComparison.Ordinal);
	}

	private static TypeReference unwrapGenericInstance(TypeReference type) =>
		(type is GenericInstanceType generic) ? generic.ElementType : type;

	private static bool isNestedInSameType(TypeDefinition candidate, TypeDefinition declaringType) {
		TypeDefinition? current = candidate.DeclaringType;
		while (current is not null) {
			if (current.FullName == declaringType.FullName)
				return true;
			current = current.DeclaringType;
		}
		return false;
	}

	private static bool nameLooksLikeStateMachineFor(TypeDefinition candidate, MethodDefinition wrapperMethod) {
		string name = candidate.Name;
		if (!GeneratedCodePolicy.NameLooksCompilerGenerated(name))
			return false;
		return name.Contains(wrapperMethod.Name, StringComparison.Ordinal);
	}

	private static bool hasMethod(TypeDefinition type, string name) {
		foreach (MethodDefinition method in type.Methods)
			if (method.Name == name)
				return true;
		return false;
	}

	private static bool hasStateField(TypeDefinition type) {
		foreach (FieldDefinition field in type.Fields) {
			if (field.FieldType.FullName != "System.Int32")
				continue;
			if (field.Name == "<>1__state" || field.Name.Contains("__state", StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	private static bool implementsInterface(TypeDefinition type, string interfaceFullName) {
		foreach (InterfaceImplementation ii in type.Interfaces) {
			TypeReference interfaceType = unwrapGenericInstance(ii.InterfaceType);
			if (interfaceType.FullName == interfaceFullName)
				return true;
		}
		return false;
	}

	private static TypeDefinition? tryResolve(TypeReference type) {
		try {
			return type.Resolve();
		} catch {
			return null;
		}
	}

	private static void addOrUpgrade(ResultDict result, string fullName, PublicizedStateMachineKind kind) {
		if (!result.TryGetValue(fullName, out PublicizedStateMachineKind existing)) {
			result.Add(fullName, kind);
			return;
		}
		if (existing == kind)
			return;
		if (kind == PublicizedStateMachineKind.AsyncIterator || existing == PublicizedStateMachineKind.AsyncIterator) {
			result[fullName] = PublicizedStateMachineKind.AsyncIterator;
			return;
		}
		result[fullName] = PublicizedStateMachineKind.Unknown;
	}
}
