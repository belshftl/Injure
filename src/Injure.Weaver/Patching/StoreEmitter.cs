// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Weaver.Model;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Injure.Weaver.Patching;

public static class StoreEmitter {
	public const string StoreName = "__Injure_MethodTargetStore";

	public static TypeDefinition Emit(
		ModuleDefinition module,
		InjureReferences ij,
		IReadOnlyList<TargetCandidate> candidates,
		IReadOnlyDictionary<TargetCandidate, TypeDefinition> delegateTypes,
		string ns
	) {
		if (findTopLevelType(module, ns, StoreName) is not null)
			throw new InvalidOperationException($"type '{ns}.{StoreName}' already exists; run this tool on a clean assembly output");

		TypeDefinition storeType = new(
			ns,
			StoreName,
			TypeAttributes.Public |
			TypeAttributes.Abstract |
			TypeAttributes.Sealed |
			TypeAttributes.BeforeFieldInit,
			module.TypeSystem.Object
		);
		module.Types.Add(storeType);

		ArrayType methodTargetArrayType = new(ij.MethodTargetDefinitionType);
		FieldDefinition targetsField = new(
			"targets",
			FieldAttributes.Private |
			FieldAttributes.Static |
			FieldAttributes.InitOnly,
			methodTargetArrayType
		);
		storeType.Fields.Add(targetsField);

		emitCctor(module, ij, storeType, targetsField, candidates, delegateTypes);
		emitEnumerate(storeType, targetsField, methodTargetArrayType);
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
		IReadOnlyList<TargetCandidate> candidates,
		IReadOnlyDictionary<TargetCandidate, TypeDefinition> delegateTypes
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
			typeof(MethodBase).GetMethod(
				nameof(MethodBase.GetMethodFromHandle),
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
		il.Emit(OpCodes.Newarr, ij.MethodTargetDefinitionType);

		for (int i = 0; i < candidates.Count; i++) {
			TargetCandidate candidate = candidates[i];
			TypeDefinition nextDelegateType = delegateTypes[candidate];

			il.Emit(OpCodes.Dup);
			il.Emit(OpCodes.Ldc_I4, i);
			il.Emit(OpCodes.Ldstr, candidate.ID);
			il.Emit(OpCodes.Ldtoken, module.ImportReference(candidate.Method));
			il.Emit(OpCodes.Call, getMethodFromHandle);
			il.Emit(OpCodes.Ldtoken, nextDelegateType);
			il.Emit(OpCodes.Call, getTypeFromHandle);
			il.Emit(OpCodes.Newobj, ij.MethodTargetDefinitionCtor);
			il.Emit(OpCodes.Stelem_Any, ij.MethodTargetDefinitionType);
		}

		il.Emit(OpCodes.Stsfld, targetsField);
		il.Emit(OpCodes.Ret);

		storeType.Methods.Add(cctor);
	}

	private static void emitEnumerate(
		TypeDefinition storeType,
		FieldDefinition targetsField,
		TypeReference methodTargetArrayType
	) {
		MethodDefinition method = new(
			"Enumerate",
			MethodAttributes.Public |
			MethodAttributes.Static |
			MethodAttributes.HideBySig,
			methodTargetArrayType
		);

		ILProcessor il = method.Body.GetILProcessor();
		il.Emit(OpCodes.Ldsfld, targetsField);
		il.Emit(OpCodes.Ret);

		storeType.Methods.Add(method);
	}

	private static void addAssemblyStoreAttribute(ModuleDefinition module, InjureReferences ij, TypeDefinition storeType) {
		CustomAttribute attribute = new(ij.ModMethodTargetStoreAttributeCtor);
		attribute.ConstructorArguments.Add(
			new CustomAttributeArgument(
				module.ImportReference(typeof(Type)),
				storeType
			)
		);
		module.Assembly.CustomAttributes.Add(attribute);
	}
}
