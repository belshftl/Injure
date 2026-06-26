// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Mono.Cecil;
using Mono.Cecil.Cil;

using Injure.Weaver.Model;

namespace Injure.Weaver.Patching;

public static class StoreEmitter {
	public const string HtsName = "__Injure_HookTargetStore";

	public static TypeDefinition Emit(
		ModuleDefinition module,
		InjureReferences ij,
		IReadOnlyList<HookCandidate> candidates,
		IReadOnlyDictionary<HookCandidate, TypeDefinition> delegateTypes,
		string ns
	) {
		if (findTopLevelType(module, ns, HtsName) is not null)
			throw new InvalidOperationException($"type '{ns}.{HtsName}' already exists; run this tool on a clean assembly output");

		TypeDefinition storeType = new(
			ns,
			HtsName,
			TypeAttributes.Public |
			TypeAttributes.Abstract |
			TypeAttributes.Sealed |
			TypeAttributes.BeforeFieldInit,
			module.TypeSystem.Object
		);
		module.Types.Add(storeType);

		ArrayType hookTargetArrayType = new(ij.HookTargetType);
		FieldDefinition targetsField = new(
			"targets",
			FieldAttributes.Private |
			FieldAttributes.Static |
			FieldAttributes.InitOnly,
			hookTargetArrayType
		);
		storeType.Fields.Add(targetsField);

		emitCctor(module, ij, storeType, targetsField, candidates, delegateTypes);
		emitEnumerate(storeType, targetsField, hookTargetArrayType);
		addAssemblyStoreAttribute(module, ij, storeType);
		return storeType;
	}

	private static TypeDefinition? findTopLevelType(ModuleDefinition module, string ns, string name) {
		foreach (TypeDefinition type in module.Types)
			if (type.Namespace == ns && type.Name == name)
				return type;
		return null;
	}

	private static void emitCctor(
		ModuleDefinition module,
		InjureReferences ij,
		TypeDefinition storeType,
		FieldDefinition targetsField,
		IReadOnlyList<HookCandidate> candidates,
		IReadOnlyDictionary<HookCandidate, TypeDefinition> delegateTypes
	) {
		MethodDefinition cctor = new(
			".cctor",
			MethodAttributes.Private |
			MethodAttributes.Static |
			MethodAttributes.HideBySig |
			MethodAttributes.SpecialName |
			MethodAttributes.RTSpecialName,
			module.TypeSystem.Void
		);

		ILProcessor il = cctor.Body.GetILProcessor();
		MethodReference getMethodFromHandle = module.ImportReference(
			typeof(System.Reflection.MethodBase).GetMethod(
				nameof(System.Reflection.MethodBase.GetMethodFromHandle),
				new[] { typeof(RuntimeMethodHandle) }
			)
		);
		MethodReference getTypeFromHandle = module.ImportReference(
			typeof(Type).GetMethod(
				nameof(Type.GetTypeFromHandle),
				new[] { typeof(RuntimeTypeHandle) }
			)
		);

		il.Emit(OpCodes.Ldc_I4, candidates.Count);
		il.Emit(OpCodes.Newarr, ij.HookTargetType);

		for (int i = 0; i < candidates.Count; i++) {
			HookCandidate candidate = candidates[i];
			TypeDefinition origDelegateType = delegateTypes[candidate];

			il.Emit(OpCodes.Dup);
			il.Emit(OpCodes.Ldc_I4, i);
			il.Emit(OpCodes.Ldstr, candidate.ID);
			il.Emit(OpCodes.Ldtoken, module.ImportReference(candidate.Method));
			il.Emit(OpCodes.Call, getMethodFromHandle);
			il.Emit(OpCodes.Ldtoken, origDelegateType);
			il.Emit(OpCodes.Call, getTypeFromHandle);
			il.Emit(OpCodes.Newobj, ij.HookTargetCtor);
			il.Emit(OpCodes.Stelem_Any, ij.HookTargetType);
		}

		il.Emit(OpCodes.Stsfld, targetsField);
		il.Emit(OpCodes.Ret);

		storeType.Methods.Add(cctor);
	}

	private static void emitEnumerate(
		TypeDefinition storeType,
		FieldDefinition targetsField,
		TypeReference hookTargetArrayType
	) {
		MethodDefinition method = new(
			"Enumerate",
			MethodAttributes.Public |
			MethodAttributes.Static |
			MethodAttributes.HideBySig,
			hookTargetArrayType
		);

		ILProcessor il = method.Body.GetILProcessor();
		il.Emit(OpCodes.Ldsfld, targetsField);
		il.Emit(OpCodes.Ret);

		storeType.Methods.Add(method);
	}

	private static void addAssemblyStoreAttribute(ModuleDefinition module, InjureReferences ij, TypeDefinition storeType) {
		CustomAttribute attribute = new(ij.ModHookTargetStoreAttributeCtor);
		attribute.ConstructorArguments.Add(
			new CustomAttributeArgument(
				module.ImportReference(typeof(Type)),
				storeType
			)
		);
		module.Assembly.CustomAttributes.Add(attribute);
	}
}
