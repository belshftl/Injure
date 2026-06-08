// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Mono.Cecil;

using Injure.Weaver.Model;

namespace Injure.Weaver.Patching;

public static class HookEmitter {
	public static Dictionary<HookCandidate, TypeDefinition> Emit(
		ModuleDefinition module,
		IReadOnlyList<HookCandidate> candidates,
		string hooksRootFullName,
		string rawHooksRootFullName
	) {
		TypeDefinition hooksRoot = findOrCreateRoot(module, hooksRootFullName);
		TypeDefinition rawHooksRoot = findOrCreateRoot(module, rawHooksRootFullName);
		Dictionary<HookCandidate, TypeDefinition> delegateTypes = new();

		foreach (HookCandidate candidate in candidates) {
			TypeDefinition root = candidate.Kind == HookKind.Intended ? hooksRoot : rawHooksRoot;
			TypeDefinition container = findOrCreateNestedStaticClass(module, root, candidate.ContainerName);

			addStringConstant(module, container, candidate.ConstantName, candidate.ID);

			TypeDefinition origDelegate = createOrigDelegate(module, candidate);
			container.NestedTypes.Add(origDelegate);
			delegateTypes.Add(candidate, origDelegate);
		}

		return delegateTypes;
	}

	private static TypeDefinition findOrCreateRoot(ModuleDefinition module, string fullName) {
		(string ns, string name) = TypeNameUtil.SplitFullTypeName(fullName);

		foreach (TypeDefinition type in module.Types)
			if (type.Namespace == ns && type.Name == name)
				return type;

		TypeDefinition root = new(
			ns,
			name,
			TypeAttributes.Public |
			TypeAttributes.Abstract |
			TypeAttributes.Sealed |
			TypeAttributes.BeforeFieldInit,
			module.TypeSystem.Object
		);

		module.Types.Add(root);
		return root;
	}

	private static TypeDefinition findOrCreateNestedStaticClass(ModuleDefinition module, TypeDefinition root, string name) {
		foreach (TypeDefinition nested in root.NestedTypes)
			if (nested.Name == name)
				return nested;

		TypeDefinition type = new(
			"",
			name,
			TypeAttributes.NestedPublic |
			TypeAttributes.Abstract |
			TypeAttributes.Sealed |
			TypeAttributes.BeforeFieldInit,
			module.TypeSystem.Object
		);

		root.NestedTypes.Add(type);
		return type;
	}

	private static void addStringConstant(ModuleDefinition module, TypeDefinition container, string name, string value) {
		foreach (FieldDefinition field in container.Fields)
			if (field.Name == name)
				throw new InvalidOperationException($"failed to emit '{container.FullName}.{name}' because it already exists");

		FieldDefinition fieldDefinition = new(
			name,
			FieldAttributes.Public |
			FieldAttributes.Static |
			FieldAttributes.Literal |
			FieldAttributes.HasDefault,
			module.TypeSystem.String
		) {
			Constant = value,
		};

		container.Fields.Add(fieldDefinition);
	}

	private static TypeDefinition createOrigDelegate(ModuleDefinition module, HookCandidate candidate) {
		TypeDefinition delegateType = new(
			"",
			candidate.OrigDelegateName,
			TypeAttributes.NestedPublic |
			TypeAttributes.Sealed |
			TypeAttributes.AnsiClass |
			TypeAttributes.AutoClass,
			module.ImportReference(typeof(MulticastDelegate))
		);

		MethodDefinition ctor = new(
			".ctor",
			MethodAttributes.Public |
			MethodAttributes.HideBySig |
			MethodAttributes.SpecialName |
			MethodAttributes.RTSpecialName,
			module.TypeSystem.Void
		);
		ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
		ctor.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(IntPtr))));
		ctor.ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed;
		delegateType.Methods.Add(ctor);

		MethodDefinition invoke = new(
			"Invoke",
			MethodAttributes.Public |
			MethodAttributes.HideBySig |
			MethodAttributes.NewSlot |
			MethodAttributes.Virtual,
			module.ImportReference(candidate.Method.ReturnType)
		) {
			ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
		};

		foreach (ParameterDefinition parameter in getOrigDelegateParameters(module, candidate.Method))
			invoke.Parameters.Add(parameter);

		delegateType.Methods.Add(invoke);
		return delegateType;
	}

	private static IEnumerable<ParameterDefinition> getOrigDelegateParameters(ModuleDefinition module, MethodDefinition method) {
		if (method.HasThis) {
			TypeReference selfType = module.ImportReference(method.DeclaringType);

			if (method.DeclaringType.IsValueType)
				selfType = new ByReferenceType(selfType);

			yield return new ParameterDefinition("self", ParameterAttributes.None, selfType);
		}

		foreach (ParameterDefinition parameter in method.Parameters)
			yield return new ParameterDefinition(parameter.Name, parameter.Attributes, module.ImportReference(parameter.ParameterType));
	}
}
