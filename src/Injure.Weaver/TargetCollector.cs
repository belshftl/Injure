// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Injure.Weaver;

public sealed class TargetCollector(Options options) {
	private readonly Options options = options;

	/// <summary>
	/// Compiler-generated types reached through a kickoff, not mirrored by themselves.
	/// </summary>
	private readonly HashSet<TypeDefinition> claimed = new();

	public List<string> Skipped { get; } = new();

	public List<MirrorScope> Collect(ModuleDefinition module) {
		foreach (TypeDefinition type in allTypes(module))
			claimStateMachines(type);

		List<MirrorScope> roots = new();
		foreach (TypeDefinition type in module.TopLevelTypes)
			if (build(type) is MirrorScope scope)
				roots.Add(scope);
		return roots;
	}

	private static IEnumerable<TypeDefinition> allTypes(ModuleDefinition module) {
		Stack<TypeDefinition> stack = new(module.TopLevelTypes);
		while (stack.Count > 0) {
			TypeDefinition t = stack.Pop();
			yield return t;
			foreach (TypeDefinition n in t.NestedTypes)
				stack.Push(n);
		}
	}

	private void claimStateMachines(TypeDefinition type) {
		foreach (MethodDefinition method in type.Methods)
			if (StateMachineTypeOf(method) is TypeDefinition sm)
				claimed.Add(sm);
	}

	private MirrorScope? build(TypeDefinition type) {
		if (claimed.Contains(type) || IsClosureType(type))
			return null;

		MirrorScope scope = new() { Source = type };

		if (options.MatchesTargetFilter(type.FullName)) {
			HashSet<MethodDefinition> eImpls = explicitImpls(type);
			foreach (MethodDefinition method in type.Methods) {
				if (eImpls.Contains(method))
					continue;
				addGroups(scope, type, method);
			}
			foreach (TypeDefinition nested in type.NestedTypes) {
				if (!IsClosureType(nested))
					continue;
				foreach (MethodDefinition method in nested.Methods)
					addGroups(scope, type, method, selfAsObject: true);
			}
		}

		// signature accessibility is a C# rule, not an ecma-335 one
		foreach (TypeDefinition nested in type.NestedTypes)
			if (build(nested) is MirrorScope child)
				scope.Children.Add(child);

		return scope.Groups.Count > 0 || scope.Children.Count > 0 ? scope : null;
	}

	private void addGroups(
		MirrorScope scope,
		TypeDefinition mirrorOwner,
		MethodDefinition method,
		bool selfAsObject = false
	) {
		if (!IsPatchable(method))
			return;

		// a delegate type can't be of the vararg callconv
		if ((method.Signature!.Attributes & CallingConventionAttributes.VarArg) != 0) {
			Skipped.Add($"{method.FullName}: vararg calling convention");
			return;
		}

		string? baseName = baseNameOf(method, out TargetKind kind);
		if (baseName is null)
			return;
		if (kind == TargetKind.Lambda && !options.IncludeLambdas)
			return;

		NameGroup group = new() {
			Method = method,
			MirrorOwner = mirrorOwner,
			BaseName = baseName,
			CanonicalKey = TypeNameRenderer.Canonical(method),
		};
		group.Targets.Add(
			new Target {
				Method = method,
				Kind = kind,
				Suffix = "",
				SelfAsObject = selfAsObject || IsClosureType(method.DeclaringType!),
			}
		);

		if (StateMachineTypeOf(method) is TypeDefinition sm) {
			if (sm.Methods.FirstOrDefault(static m => m.Name == "MoveNext" && IsPatchable(m)) is MethodDefinition moveNext)
				group.Targets.Add(
					new Target {
						Method = moveNext,
						Kind = TargetKind.StateMachineMoveNext,
						Suffix = "__StateMachine",
						SelfAsObject = true,
					}
				);
			if (sm.Methods.FirstOrDefault(static m => (
				m.Name == "Dispose"
				|| (m.Name?.Value.EndsWith(".Dispose", StringComparison.Ordinal) ?? false)
			) && IsPatchable(m)) is MethodDefinition dispose)
				group.Targets.Add(
					new Target {
						Method = dispose,
						Kind = TargetKind.StateMachineDispose,
						Suffix = "__StateMachine_Dispose",
						SelfAsObject = true,
					}
				);
		}

		scope.Groups.Add(group);
	}

	private static string? baseNameOf(MethodDefinition method, out TargetKind kind) {
		kind = TargetKind.Method;
		if (method.Name?.Value is not string name)
			return null;

		if (name == ".ctor")
			return "ctor";
		if (name == ".cctor")
			return "cctor";

		// <Kickoff>g__Local|12_0
		if (name.StartsWith('<') && name.Contains(">g__", StringComparison.Ordinal)) {
			int close = name.IndexOf('>');
			int start = close + 4;
			int bar = name.IndexOf('|', start);
			if (close > 1 && bar > start) {
				kind = TargetKind.LocalFunction;
				string kickoff = TypeNameRenderer.Sanitize(name[1..close]);
				string local = TypeNameRenderer.Sanitize(name[start..bar]);
				return $"{kickoff}__Local_{local}";
			}
		}

		// <Kickoff>b__12_0
		if (name.StartsWith('<') && name.Contains(">b__", StringComparison.Ordinal)) {
			int close = name.IndexOf('>');
			int start = close + 4;
			if (close > 1 && start < name.Length) {
				kind = TargetKind.Lambda;
				string kickoff = TypeNameRenderer.Sanitize(name[1..close]);
				string ordinal = TypeNameRenderer.Sanitize(name[start..]);
				return $"{kickoff}__Lambda_{ordinal}";
			}
		}

		if (name.Contains('<') || name.Contains('.'))
			return TypeNameRenderer.Sanitize(name);

		return name;
	}

	public static bool IsPatchable(MethodDefinition m) =>
		!m.IsAbstract
		&& !m.IsPInvokeImpl
		&& !m.IsInternalCall
		&& !m.IsRuntime
		&& !m.IsNative
		&& m.Signature is not null;

	private static HashSet<MethodDefinition> explicitImpls(TypeDefinition type) {
		HashSet<MethodDefinition> set = new();
		foreach (MethodImplementation impl in type.MethodImplementations)
			if (impl.Body is MethodDefinition body)
				set.Add(body);
		return set;
	}

	public static bool IsClosureType(TypeDefinition type) =>
		type.Name?.Value is string name && name.StartsWith("<>c", StringComparison.Ordinal);

	public static TypeDefinition? StateMachineTypeOf(MethodDefinition method) {
		foreach (CustomAttribute attr in method.CustomAttributes) {
			string? name = attr.Constructor?.DeclaringType?.Name?.Value;
			if (
				name is not "AsyncStateMachineAttribute"
				and not "IteratorStateMachineAttribute"
				and not "AsyncIteratorStateMachineAttribute"
			) {
				continue;
			}
			if (attr.Signature?.FixedArguments.Count is not > 0)
				continue;
			if (attr.Signature.FixedArguments[0].Element is TypeSignature sig)
				return findStateMachine(method.DeclaringType, sig);
		}
		return null;
	}

	private static TypeDefinition? findStateMachine(TypeDefinition? declaring, TypeSignature sig) {
		if (declaring is null)
			return null;

		ITypeDefOrRef? named = sig switch {
			GenericInstanceTypeSignature g => g.GenericType,
			TypeDefOrRefSignature t => t.Type,
			_ => null,
		};
		if (named is TypeDefinition direct)
			return direct;
		if (named?.Name is not AsmResolver.Utf8String name)
			return null;

		foreach (TypeDefinition nested in declaring.NestedTypes)
			if (nested.Name == name)
				return nested;
		return null;
	}
}
