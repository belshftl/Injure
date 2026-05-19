// SPDX-License-Identifier: MIT

using Mono.Cecil;

using Injure.Weaver.Model;

namespace Injure.Weaver.Patching;

public enum PublicizationKind {
	No,
	Normal,
	StateMachine,
}

public static class PublicizationPolicy {
	public static PublicizationKind ShouldPublicizeType(TypeDefinition type, in AssemblyAnalysis analysis) {
		if (analysis.StateMachineTypeFullNames.ContainsKey(type.FullName))
			return PublicizationKind.StateMachine;
		return GeneratedCodePolicy.LooksCompilerGenerated(type) ? PublicizationKind.No : PublicizationKind.Normal;
	}

	public static PublicizationKind ShouldPublicizeField(FieldDefinition field, in AssemblyAnalysis analysis) {
		if (analysis.StateMachineTypeFullNames.ContainsKey(field.DeclaringType.FullName))
			return PublicizationKind.StateMachine;
		return GeneratedCodePolicy.LooksCompilerGenerated(field) ? PublicizationKind.No : PublicizationKind.Normal;
	}

	public static PublicizationKind ShouldPublicizeMethod(MethodDefinition method, in AssemblyAnalysis analysis) {
		if (method.IsRuntimeSpecialName && method.Name == ".cctor")
			return PublicizationKind.No;
		if (analysis.StateMachineTypeFullNames.ContainsKey(method.DeclaringType.FullName))
			return PublicizationKind.StateMachine;
		if (MethodUtil.IsPropertyAccessor(method) || MethodUtil.IsEventAccessor(method))
			return PublicizationKind.Normal;
		return GeneratedCodePolicy.LooksCompilerGenerated(method) ? PublicizationKind.No : PublicizationKind.Normal;
	}
}
