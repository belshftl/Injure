// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.InteropServices;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.MethodModification;

internal sealed class MethodModificationValidationException(string message) : InvalidOperationException(message);

internal static class DetourMethodValidator {
	private readonly record struct ParamSpec(Type Type, bool IsOut) {
		public override string ToString() {
			string prefix = IsOut ? "out " : Type.IsByRef ? "ref " : "";
			Type type = Type.IsByRef ? Type.GetElementType()! : Type;
			return prefix + MethodDisplay.FormatType(type);
		}
	}

	public static void ValidateGeneratedDetourMethod(MethodInfo detourMethod, MethodTargetDefinition target) {
		validateDetourMethodCommon(detourMethod, "detour");
		validateDetourTarget(target.Method, target.TargetId);

		MethodInfo nextInvoke = getDelegateInvoke(target.NextDelegateType);
		validateDetourAgainstNextDelegate(detourMethod, target.NextDelegateType, nextInvoke, target.TargetId);
	}

	public static void ValidateDirectDetourMethod(MethodInfo detourMethod, MethodBase targetMethod) {
		validateDetourMethodCommon(detourMethod, "direct detour");
		string targetDisplay = MethodDisplay.FormatMethod(targetMethod);
		validateDetourTarget(targetMethod, targetDisplay);

		ParameterInfo[] detourParams = detourMethod.GetParameters();
		if (detourParams.Length == 0)
			throw new MethodModificationValidationException(
				$"direct detour method '{MethodDisplay.FormatMethod(detourMethod)}' must take a next delegate as its first parameter"
			);

		Type nextDelegateType = detourParams[0].ParameterType;
		if (!typeof(Delegate).IsAssignableFrom(nextDelegateType))
			throw new MethodModificationValidationException(
				$"first parameter of direct detour method '{MethodDisplay.FormatMethod(detourMethod)}' must be a delegate type (found '{MethodDisplay.FormatType(nextDelegateType)}' instead)"
			);

		MethodInfo nextInvoke = getDelegateInvoke(nextDelegateType);
		validateNextDelegateAgainstTarget(nextDelegateType, nextInvoke, targetMethod, targetDisplay);
		validateDetourAgainstNextDelegate(detourMethod, nextDelegateType, nextInvoke, targetDisplay);
	}

	private static void validateDetourMethodCommon(MethodInfo method, string role) {
		if (!method.IsStatic)
			throw new MethodModificationValidationException($"{role} method '{MethodDisplay.FormatMethod(method)}' must be static");
		if (method.ContainsGenericParameters)
			throw new MethodModificationValidationException($"{role} method '{MethodDisplay.FormatMethod(method)}' must not have unbound generic parameters");
		if (method.IsGenericMethodDefinition)
			throw new MethodModificationValidationException($"{role} method '{MethodDisplay.FormatMethod(method)}' must not be an open generic method");
	}

	private static void validateDetourTarget(MethodBase targetMethod, string targetDescription) {
		if (targetMethod.ContainsGenericParameters)
			throw new MethodModificationValidationException($"detour target '{targetDescription}' must be monomorphic");
		if ((targetMethod.CallingConvention & CallingConventions.VarArgs) != 0)
			throw new MethodModificationValidationException($"detour target '{targetDescription}' uses the unsupported managed varargs calling convention");
		if ((targetMethod.CallingConvention & CallingConventions.ExplicitThis) != 0)
			throw new MethodModificationValidationException($"detour target '{targetDescription}' uses the unsupported explicit-this calling convention");
		if (targetMethod.IsAbstract)
			throw new MethodModificationValidationException($"detour target '{targetDescription}' is abstract");
		if (targetMethod is ConstructorInfo { IsStatic: true })
			throw new MethodModificationValidationException($"detour target '{targetDescription}' cannot be a static constructor");
		if (targetMethod.IsDefined(typeof(UnmanagedCallersOnlyAttribute), inherit: false))
			throw new MethodModificationValidationException($"detour target '{targetDescription}' is marked UnmanagedCallersOnly");
	}

	private static void validateDetourAgainstNextDelegate(MethodInfo detourMethod, Type nextDelegateType, MethodInfo nextInvoke, string targetDescription) {
		ParameterInfo[] detourParams = detourMethod.GetParameters();
		ParameterInfo[] nextParams = nextInvoke.GetParameters();

		if (detourParams.Length != nextParams.Length + 1)
			throw new MethodModificationValidationException(
				$"detour method '{MethodDisplay.FormatMethod(detourMethod)}' for target '{targetDescription}' must take {nextParams.Length + 1} parameter(s): the next delegate '{MethodDisplay.FormatType(nextDelegateType)}' followed by the original call parameters (found {detourParams.Length} parameter(s) instead)"
			);

		if (detourParams[0].ParameterType != nextDelegateType)
			throw new MethodModificationValidationException(
				$"parameter 0 of detour method '{MethodDisplay.FormatMethod(detourMethod)}' for target '{targetDescription}' must be next delegate type '{MethodDisplay.FormatType(nextDelegateType)}' (found '{MethodDisplay.FormatType(detourParams[0].ParameterType)}' instead)"
			);

		for (int i = 0; i < nextParams.Length; i++) {
			ParameterInfo expected = nextParams[i];
			ParameterInfo actual = detourParams[i + 1];
			if (!sameParameterType(actual, expected))
				throw new MethodModificationValidationException(
					$"parameter {i + 1} of detour method '{MethodDisplay.FormatMethod(detourMethod)}' for target '{targetDescription}' must be '{MethodDisplay.FormatParameter(expected)}' (found '{MethodDisplay.FormatParameter(actual)}' instead)"
				);
		}

		if (detourMethod.ReturnType != nextInvoke.ReturnType)
			throw new MethodModificationValidationException(
				$"detour method '{MethodDisplay.FormatMethod(detourMethod)}' for target '{targetDescription}' must return '{MethodDisplay.FormatType(nextInvoke.ReturnType)}' (found '{MethodDisplay.FormatType(detourMethod.ReturnType)}' instead)"
			);
	}

	private static void validateNextDelegateAgainstTarget(Type nextDelegateType, MethodInfo nextInvoke, MethodBase targetMethod, string targetDescription) {
		Type expectedReturn = getTargetReturnType(targetMethod);
		ParamSpec[] expectedParams = getExpectedNextDelegateParameters(targetMethod);
		if (nextInvoke.ReturnType != expectedReturn)
			throw new MethodModificationValidationException(
				$"next delegate '{MethodDisplay.FormatType(nextDelegateType)}' for direct detour target '{targetDescription}' must return '{MethodDisplay.FormatType(expectedReturn)}' (found '{MethodDisplay.FormatType(nextInvoke.ReturnType)}' instead)"
			);

		ParameterInfo[] actualParams = nextInvoke.GetParameters();
		if (actualParams.Length != expectedParams.Length)
			throw new MethodModificationValidationException(
				$"next delegate '{MethodDisplay.FormatType(nextDelegateType)}' for direct detour target '{targetDescription}' must have {expectedParams.Length} parameter(s) (found {actualParams.Length} parameter(s) instead)"
			);

		for (int i = 0; i < expectedParams.Length; i++) {
			ParamSpec expected = expectedParams[i];
			ParameterInfo actual = actualParams[i];
			if (actual.ParameterType != expected.Type || actual.IsOut != expected.IsOut)
				throw new MethodModificationValidationException(
					$"parameter {i} of next delegate '{MethodDisplay.FormatType(nextDelegateType)}' for direct detour target '{targetDescription}' must be '{expected}' (found '{MethodDisplay.FormatParameter(actual)}' instead)"
				);
		}
	}

	private static MethodInfo getDelegateInvoke(Type delegateType) {
		if (!typeof(Delegate).IsAssignableFrom(delegateType))
			throw new MethodModificationValidationException($"type '{MethodDisplay.FormatType(delegateType)}' is not a delegate type");
		MethodInfo invoke = delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance) ??
			throw new MethodModificationValidationException($"delegate type '{MethodDisplay.FormatType(delegateType)}' unexpectedly does not expose Invoke");
		if (invoke.ContainsGenericParameters)
			throw new MethodModificationValidationException($"delegate type '{MethodDisplay.FormatType(delegateType)}' has unbound generic parameters");
		return invoke;
	}

	private static Type getTargetReturnType(MethodBase targetMethod) {
		return targetMethod switch {
			MethodInfo method => method.ReturnType,
			ConstructorInfo => typeof(void),
			_ => throw new MethodModificationValidationException($"unsupported detour target type '{targetMethod.GetType().FullName}'"),
		};
	}

	private static ParamSpec[] getExpectedNextDelegateParameters(MethodBase targetMethod) {
		List<ParamSpec> result = new();
		if (!targetMethod.IsStatic) {
			Type declaringType = targetMethod.DeclaringType ??
				throw new MethodModificationValidationException($"instance detour target '{MethodDisplay.FormatMethod(targetMethod)}' has no declaring type");
			Type receiverType = declaringType.IsValueType ? declaringType.MakeByRefType() : declaringType;
			result.Add(new ParamSpec(receiverType, IsOut: false));
		}
		foreach (ParameterInfo param in targetMethod.GetParameters())
			result.Add(new ParamSpec(param.ParameterType, param.IsOut));
		return result.ToArray();
	}

	private static bool sameParameterType(ParameterInfo actual, ParameterInfo expected) =>
		actual.ParameterType == expected.ParameterType && actual.IsOut == expected.IsOut;
}

internal static class PatchMethodValidator {
	public static void ValidateTarget(MethodBase targetMethod, string targetDescription) =>
		validatePatchTarget(targetMethod, targetDescription);

	public static void ValidateGeneratedPatchMethod(MethodInfo manipulatorMethod, MethodTargetDefinition target, Type lifetimeIdentityType) {
		validatePatchTarget(target.Method, target.TargetId);
		validatePatchMethod(manipulatorMethod, lifetimeIdentityType, target.TargetId);
	}

	public static void ValidateDirectPatchMethod(MethodInfo manipulatorMethod, MethodBase targetMethod, Type lifetimeIdentityType) {
		string targetDisplay = MethodDisplay.FormatMethod(targetMethod);
		validatePatchTarget(targetMethod, targetDisplay);
		validatePatchMethod(manipulatorMethod, lifetimeIdentityType, targetDisplay);
	}

	private static void validatePatchMethod(MethodInfo manipulatorMethod, Type lifetimeIdentityType, string targetDescription) {
		if (!manipulatorMethod.IsStatic)
			throw new MethodModificationValidationException($"patch method '{MethodDisplay.FormatMethod(manipulatorMethod)}' must be static");
		if (manipulatorMethod.ContainsGenericParameters)
			throw new MethodModificationValidationException($"patch method '{MethodDisplay.FormatMethod(manipulatorMethod)}' must not have unbound generic parameters");
		if (manipulatorMethod.IsGenericMethodDefinition)
			throw new MethodModificationValidationException($"patch method '{MethodDisplay.FormatMethod(manipulatorMethod)}' must not be an open generic method");
		if (manipulatorMethod.ReturnType != typeof(void))
			throw new MethodModificationValidationException($"patch method '{MethodDisplay.FormatMethod(manipulatorMethod)}' for target '{targetDescription}' must return void");

		Type expectedContextType = typeof(IlContext<>).MakeGenericType(lifetimeIdentityType);
		ParameterInfo[] parameters = manipulatorMethod.GetParameters();
		if (parameters.Length != 1)
			throw new MethodModificationValidationException(
				$"patch method '{MethodDisplay.FormatMethod(manipulatorMethod)}' for target '{targetDescription}' must take exactly one parameter of type '{MethodDisplay.FormatType(expectedContextType)}'"
			);
		if (parameters[0].ParameterType != expectedContextType)
			throw new MethodModificationValidationException(
				$"patch method '{MethodDisplay.FormatMethod(manipulatorMethod)}' for target '{targetDescription}' must take '{MethodDisplay.FormatType(expectedContextType)}' (found '{MethodDisplay.FormatType(parameters[0].ParameterType)}' instead)"
			);
	}

	private static void validatePatchTarget(MethodBase targetMethod, string targetDescription) {
		if (targetMethod.IsGenericMethod || targetMethod.DeclaringType?.IsGenericType == true)
			throw new MethodModificationValidationException($"patch target '{targetDescription}' is generic; generic patch targets are not supported yet");
		if ((targetMethod.CallingConvention & CallingConventions.VarArgs) != 0)
			throw new MethodModificationValidationException($"patch target '{targetDescription}' uses the unsupported managed varargs calling convention");
		if (targetMethod.IsAbstract)
			throw new MethodModificationValidationException($"patch target '{targetDescription}' is abstract and has no method body");
		if (targetMethod is ConstructorInfo { IsStatic: true })
			throw new MethodModificationValidationException($"patch target '{targetDescription}' cannot be a static constructor; static constructors are not supported yet");
	}
}

internal static class MethodDisplay {
	public static string FormatMethod(MethodBase method) {
		string declaring = method.DeclaringType?.FullName ?? "<unknown>";
		string parameters = string.Join(", ", method.GetParameters().Select(static p => FormatType(p.ParameterType)));
		return $"{declaring}.{method.Name}({parameters})";
	}

	public static string FormatType(Type type) {
		if (type.IsByRef)
			return FormatType(type.GetElementType()!) + "&";
		return type.FullName ?? type.Name;
	}

	public static string FormatParameter(ParameterInfo parameter) {
		string prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "";
		Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
		return prefix + FormatType(type);
	}
}
