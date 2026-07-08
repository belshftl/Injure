// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions.Hooks.Il;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.Hooks;

internal sealed class HookValidationException(string message) : InvalidOperationException(message);

internal static class HookMethodValidator {
	private readonly record struct ParamSpec(Type Type, bool IsOut) {
		public override string ToString() {
			string prefix = IsOut ? "out " : Type.IsByRef ? "ref " : "";
			Type type = Type.IsByRef ? Type.GetElementType()! : Type;
			return prefix + formatType(type);
		}
	}

	public static void ValidateGeneratedManagedHookMethod(MethodInfo hookMethod, HookTargetDefinition target) {
		validatePatchMethodCommon(hookMethod, "hook");

		MethodInfo nextInvoke = getDelegateInvoke(target.NextDelegateType);
		validateHookReplacementAgainstNextDelegate(hookMethod, target.NextDelegateType, nextInvoke, target.TargetId);
	}

	public static void ValidateDirectManagedHookMethod(MethodInfo hookMethod, MethodBase targetMethod) {
		validatePatchMethodCommon(hookMethod, "direct hook");

		ParameterInfo[] hookParams = hookMethod.GetParameters();
		if (hookParams.Length == 0)
			throw new HookValidationException($"direct hook method '{FormatMethod(hookMethod)}' must take a next delegate as its first parameter");

		Type nextDelegateType = hookParams[0].ParameterType;
		if (!typeof(Delegate).IsAssignableFrom(nextDelegateType)) {
			throw new HookValidationException(
				$"first parameter of direct hook method '{FormatMethod(hookMethod)}' must be a delegate type " +
				$"(found '{formatType(nextDelegateType)}' instead)"
			);
		}

		MethodInfo nextInvoke = getDelegateInvoke(nextDelegateType);
		validateNextDelegateAgainstTarget(nextDelegateType, nextInvoke, targetMethod, FormatMethod(targetMethod));
		validateHookReplacementAgainstNextDelegate(hookMethod, nextDelegateType, nextInvoke, FormatMethod(targetMethod));
	}

	public static void ValidateGeneratedIlHookMethod(MethodInfo manipulatorMethod, HookTargetDefinition target, Type lifetimeIdentityType) {
		validateIlHookMethod(manipulatorMethod, lifetimeIdentityType, target.TargetId);
	}

	public static void ValidateDirectIlHookMethod(MethodInfo manipulatorMethod, MethodBase targetMethod, Type lifetimeIdentityType) {
		validateIlHookMethod(manipulatorMethod, lifetimeIdentityType, FormatMethod(targetMethod));
	}

	private static void validatePatchMethodCommon(MethodInfo method, string role) {
		if (!method.IsStatic)
			throw new HookValidationException($"{role} method '{FormatMethod(method)}' must be static");
		if (method.ContainsGenericParameters)
			throw new HookValidationException($"{role} method '{FormatMethod(method)}' must not have unbound generic parameters");
		if (method.IsGenericMethodDefinition)
			throw new HookValidationException($"{role} method '{FormatMethod(method)}' must not be an open generic method");
		if (method.ReturnParameter.ParameterType.IsByRef)
			throw new HookValidationException($"{role} method '{FormatMethod(method)}' uses a byref return; this is not supported");
	}

	private static void validateIlHookMethod(MethodInfo manipulatorMethod, Type lifetimeIdentityType, string targetDescription) {
		validatePatchMethodCommon(manipulatorMethod, "IL hook");

		if (manipulatorMethod.ReturnType != typeof(void))
			throw new HookValidationException($"IL hook method '{FormatMethod(manipulatorMethod)}' for target '{targetDescription}' must return void");

		Type expectedContextType = typeof(IlContext<>).MakeGenericType(lifetimeIdentityType);
		ParameterInfo[] @params = manipulatorMethod.GetParameters();
		if (@params.Length != 1)
			throw new HookValidationException($"IL hook method '{FormatMethod(manipulatorMethod)}' for target '{targetDescription}' must take exactly one parameter of type '{formatType(expectedContextType)}'");
		if (@params[0].ParameterType != expectedContextType)
			throw new HookValidationException($"IL hook method '{FormatMethod(manipulatorMethod)}' for target '{targetDescription}' must take '{formatType(expectedContextType)}' (found '{formatType(@params[0].ParameterType)}' instead)");
	}

	private static void validateHookReplacementAgainstNextDelegate(
		MethodInfo hookMethod,
		Type nextDelegateType,
		MethodInfo nextInvoke,
		string targetDescription
	) {
		ParameterInfo[] hookParams = hookMethod.GetParameters();
		ParameterInfo[] nextParams = nextInvoke.GetParameters();

		if (hookParams.Length != nextParams.Length + 1)
			throw new HookValidationException($"hook method '{FormatMethod(hookMethod)}' for target '{targetDescription}' must take {nextParams.Length + 1} parameter(s): the next delegate '{formatType(nextDelegateType)}' followed by the original call parameters (found {hookParams.Length} parameter(s) instead)");

		if (hookParams[0].ParameterType != nextDelegateType)
			throw new HookValidationException($"parameter 0 of hook method '{FormatMethod(hookMethod)}' for target '{targetDescription}' must be next delegate type '{formatType(nextDelegateType)}' (found '{formatType(hookParams[0].ParameterType)}' instead)");

		for (int i = 0; i < nextParams.Length; i++) {
			ParameterInfo expected = nextParams[i];
			ParameterInfo actual = hookParams[i + 1];
			if (!sameParameterType(actual, expected))
				throw new HookValidationException($"parameter {i + 1} of hook method '{FormatMethod(hookMethod)}' for target '{targetDescription}' must be '{formatParameter(expected)}' (found '{formatParameter(actual)}' instead)");
		}

		if (hookMethod.ReturnType != nextInvoke.ReturnType)
			throw new HookValidationException($"hook method '{FormatMethod(hookMethod)}' for target '{targetDescription}' must return '{formatType(nextInvoke.ReturnType)}' (found '{formatType(hookMethod.ReturnType)}' instead)");
	}

	private static void validateNextDelegateAgainstTarget(Type nextDelegateType, MethodInfo nextInvoke, MethodBase targetMethod, string targetDescription) {
		if (targetMethod.ContainsGenericParameters)
			throw new HookValidationException($"direct hook target '{targetDescription}' must not have unbound generic parameters");
		if (targetMethod is ConstructorInfo ctor && ctor.IsStatic)
			throw new HookValidationException($"direct hook target '{targetDescription}' cannot be a static constructor");

		Type expectedReturn = getTargetReturnType(targetMethod);
		ParamSpec[] expectedParams = getExpectedNextDelegateParameters(targetMethod);
		if (nextInvoke.ReturnType != expectedReturn)
			throw new HookValidationException($"next delegate '{formatType(nextDelegateType)}' for direct hook target '{targetDescription}' must return '{formatType(expectedReturn)}' (found '{formatType(nextInvoke.ReturnType)}' instead)");

		ParameterInfo[] actualParams = nextInvoke.GetParameters();
		if (actualParams.Length != expectedParams.Length)
			throw new HookValidationException($"next delegate '{formatType(nextDelegateType)}' for direct hook target '{targetDescription}' must have {expectedParams.Length} parameter(s) (found {actualParams.Length} parameter(s) instead)");

		for (int i = 0; i < expectedParams.Length; i++) {
			ParamSpec expected = expectedParams[i];
			ParameterInfo actual = actualParams[i];
			if (actual.ParameterType != expected.Type || actual.IsOut != expected.IsOut)
				throw new HookValidationException($"parameter {i} of next delegate '{formatType(nextDelegateType)}' for direct hook target '{targetDescription}' must be '{expected}' (found '{formatParameter(actual)}' instead)");
		}
	}

	private static MethodInfo getDelegateInvoke(Type delegateType) {
		if (!typeof(Delegate).IsAssignableFrom(delegateType))
			throw new HookValidationException($"type '{formatType(delegateType)}' is not a delegate type");
		MethodInfo invoke = delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance) ??
			throw new HookValidationException($"delegate type '{formatType(delegateType)}' unexpectedly does not expose Invoke");
		if (invoke.ContainsGenericParameters)
			throw new HookValidationException($"delegate type '{formatType(delegateType)}' has unbound generic parameters");
		return invoke;
	}

	private static Type getTargetReturnType(MethodBase targetMethod) {
		return targetMethod switch {
			MethodInfo method => method.ReturnType,
			ConstructorInfo => typeof(void),
			_ => throw new HookValidationException($"unsupported hook target type '{targetMethod.GetType().FullName}'"),
		};
	}

	private static ParamSpec[] getExpectedNextDelegateParameters(MethodBase targetMethod) {
		List<ParamSpec> result = new();
		if (!targetMethod.IsStatic) {
			Type declaringType = targetMethod.DeclaringType ??
				throw new HookValidationException($"instance hook target '{FormatMethod(targetMethod)}' has no declaring type");
			result.Add(new ParamSpec(declaringType, IsOut: false));
		}

		foreach (ParameterInfo parameter in targetMethod.GetParameters())
			result.Add(new ParamSpec(parameter.ParameterType, parameter.IsOut));

		return result.ToArray();
	}

	private static bool sameParameterType(ParameterInfo actual, ParameterInfo expected) =>
		actual.ParameterType == expected.ParameterType && actual.IsOut == expected.IsOut;

	internal static string FormatMethod(MethodBase method) {
		string declaring = method.DeclaringType?.FullName ?? "<unknown>";
		string parameters = string.Join(", ", method.GetParameters().Select(static p => formatType(p.ParameterType)));
		return $"{declaring}.{method.Name}({parameters})";
	}

	private static string formatType(Type type) {
		if (type.IsByRef)
			return formatType(type.GetElementType()!) + "&";
		return type.FullName ?? type.Name;
	}

	private static string formatParameter(ParameterInfo parameter) {
		string prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "";
		Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
		return prefix + formatType(type);
	}
}
