// SPDX-License-Identifier: MIT

using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class MethodUtil {
	public static bool IsPropertyAccessor(MethodDefinition method) =>
		method.IsGetter || method.IsSetter ||
		method.SemanticsAttributes.HasFlag(MethodSemanticsAttributes.Getter) ||
		method.SemanticsAttributes.HasFlag(MethodSemanticsAttributes.Setter);

	public static bool IsEventAccessor(MethodDefinition method) =>
		method.IsAddOn || method.IsRemoveOn || method.IsFire ||
		method.SemanticsAttributes.HasFlag(MethodSemanticsAttributes.AddOn) ||
		method.SemanticsAttributes.HasFlag(MethodSemanticsAttributes.RemoveOn) ||
		method.SemanticsAttributes.HasFlag(MethodSemanticsAttributes.Fire);
}
