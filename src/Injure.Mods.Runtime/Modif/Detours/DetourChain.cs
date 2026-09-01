// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Injure.Mods.Runtime.Modif.Detours;

/// <summary>
/// One detour chain link's state: what it calls next, and what arguments must be narrowed back to.
/// </summary>
/// <remarks>
/// Intended to be used from generated IL.
/// </remarks>
internal sealed class DetourLinkState {
	public required string OwnerId;
	public required string LocalId;

	/// <summary>
	/// The delegate this link invokes, typed as its caller declared it.
	/// </summary>
	public Delegate? Next;

	/// <summary>
	/// The target's parameter types, which narrowed arguments must satisfy.
	/// </summary>
	public Type[] Expected = [];

	/// <summary>
	/// Everything the chain needs kept alive, hung off the head's state.
	/// </summary>
	public object? Retained;

	/// <summary>
	/// Checks one argument against the target's parameter type.
	/// </summary>
	/// <remarks>
	/// The caller is intended to follow with <c>unbox.any</c>.
	/// </remarks>
	public object? Narrow(object? value, int index) {
		Type expected = Expected[index];
		if (value is null) {
			if (expected.IsValueType)
				throw new DetourArgumentException(OwnerId, LocalId, index, expected, null);
			return null;
		}
		if (!expected.IsInstanceOfType(value))
			throw new DetourArgumentException(OwnerId, LocalId, index, expected, value);
		return value;
	}
}

/// <summary>
/// One method's detour chain, built as a set of generated thunks.
/// </summary>
/// <remarks>
/// <para>
/// No delegate type is generated. Every link is consumed as the delegate type its caller declared,
/// which is a mod's own, and the head is reached by function pointer rather than as a delegate. Otherwise,
/// 1) a generated delegate type naming a mod's types would be a non-collectible assembly referencing a
/// collectible one, and 2) <see cref="Func{T, TResult}"/> cannot express a byref parameter, which game
/// methods have constantly.
/// </para>
/// <para>
/// Links are <see cref="DynamicMethod"/>s because each closes over the next. That also retains every
/// method and type its body names, so a chain holds its impls alive by itself.
/// </para>
/// <para>
/// The current mechanism for obtaining the address of a chain head uses a private runtime method;
/// I'm gonna be honest, I have no clue what to replace that with if it ever gets removed. That
/// should probably be figured out at some point.
/// </para>
/// </remarks>
internal sealed class DetourChain {
	private static class DynamicMethodAccessor {
		// not great, but whatever we have to do
		[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetMethodDescriptor")]
		public static extern RuntimeMethodHandle GetMethodDescriptor(DynamicMethod method);
	}

	private static readonly FieldInfo nextField = typeof(DetourLinkState).GetField(
		nameof(DetourLinkState.Next), BindingFlags.Instance | BindingFlags.Public
	)!;
	private static readonly MethodInfo narrowMethod = typeof(DetourLinkState).GetMethod(
		nameof(DetourLinkState.Narrow), BindingFlags.Instance | BindingFlags.Public
	)!;
	private static readonly MethodInfo getChainStateMethod = typeof(DetourDispatch).GetMethod(
		nameof(DetourDispatch.GetChainState), BindingFlags.Static | BindingFlags.NonPublic
	)!;
	private static readonly MethodInfo pushBypassMethod = typeof(DetourDispatch).GetMethod(
		nameof(DetourDispatch.PushBypass), BindingFlags.Static | BindingFlags.NonPublic
	)!;
	private static readonly MethodInfo unwindBypassMethod = typeof(DetourDispatch).GetMethod(
		nameof(DetourDispatch.UnwindBypass), BindingFlags.Static | BindingFlags.NonPublic
	)!;

	private readonly record struct Shape(
		string OwnerId,
		string LocalId,
		MethodInfo Impl,
		Type NextType,
		Type[] Parameters
	) {
		public static Shape Validate(DetourRegistration detour, Type returnType, Type[] parameters) {
			if (detour.Impl is not MethodInfo impl || !impl.IsStatic)
				throw new ArgumentException($"detour impl '{detour}' must be a static method", nameof(detour));
			if (impl.IsGenericMethodDefinition)
				throw new ArgumentException($"detour impl '{detour}' must not be generic", nameof(detour));

			ParameterInfo[] declared = impl.GetParameters();
			if (declared.Length != parameters.Length + 1)
				throw new ArgumentException(
					$"detour impl '{detour}' takes {declared.Length} parameter(s); expected {parameters.Length + 1}, those being a `next` delegate followed by the target's parameters",
					nameof(detour)
				);

			Type nextType = declared[0].ParameterType;
			if (!nextType.IsSubclassOf(typeof(Delegate)))
				throw new ArgumentException(
					$"detour impl '{detour}' must take a `next` delegate as its first parameter, not '{nextType}'",
					nameof(detour)
				);

			if (impl.ReturnType != returnType)
				throw new ArgumentException(
					$"detour impl '{detour}' returns '{impl.ReturnType}'; expected exact match of the target's return type of '{returnType}'",
					nameof(detour)
				);

			var widened = new Type[parameters.Length];
			for (int i = 0; i < parameters.Length; i++) {
				widened[i] = declared[i + 1].ParameterType;
				if (widened[i] == parameters[i])
					continue;
				if (parameters[i].IsByRef || parameters[i].IsPointer || widened[i].IsByRef || widened[i].IsPointer)
					throw new ArgumentException(
						$"detour impl '{detour}' takes '{widened[i]}' where the target takes '{parameters[i]}'; byref/pointer parameters must match exactly",
						nameof(detour)
					);
				if (parameters[i].IsByRefLike || widened[i].IsByRefLike)
					throw new ArgumentException(
						$"detour impl '{detour}' takes '{widened[i]}' where the target takes '{parameters[i]}'; ref struct parameters must match exactly as they cannot be boxed",
						nameof(detour)
					);
				if (!widened[i].IsAssignableFrom(parameters[i]))
					throw new ArgumentException(
						$"detour impl '{detour}' takes '{widened[i]}' where the target takes '{parameters[i]}'; expected exact match or possible upcast/assignability",
						nameof(detour)
					);
			}

			validateNext(detour, nextType, returnType, widened);
			return new Shape(detour.OwnerId, detour.LocalId, impl, nextType, widened);
		}

		private static void validateNext(
			DetourRegistration detour,
			Type nextType,
			Type returnType,
			Type[] widened
		) {
			MethodInfo invoke = nextType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public) ??
				throw new ArgumentException($"detour impl '{detour}' `next` type '{nextType}' is not an invokable delegate", nameof(detour));
			ParameterInfo[] declared = invoke.GetParameters();

			if (invoke.ReturnType != returnType)
				throw new ArgumentException(
					$"detour impl '{detour}' declares `next` with a return type of '{invoke.ReturnType}'; expected exact match of the target's return type of '{returnType}'",
					nameof(detour)
				);
			if (declared.Length != widened.Length)
				throw new ArgumentException(
					$"detour impl '{detour}' declares `next` with {declared.Length} parameters, which doesn't match the impl itself, which takes {widened.Length} not counting `next`",
					nameof(detour)
				);
			for (int i = 0; i < widened.Length; i++)
				if (declared[i].ParameterType != widened[i])
					throw new ArgumentException(
						$"detour impl '{detour}' declares `next` with parameter {i} with type '{declared[i].ParameterType}', but the impl itself takes '{widened[i]}' there",
						nameof(detour)
					);
		}
	}

	private readonly List<object> retained = new();

	/// <summary>
	/// The chain's entry point, for <see cref="DetourDispatch.SetChain"/>.
	/// </summary>
	public IntPtr Entry { get; private set; }

	/// <summary>
	/// The head's state, which the head reads back out of the dispatch table.
	/// </summary>
	public DetourLinkState State { get; private set; } = null!;

	/// <summary>
	/// The head thunk, for invoking a chain directly without a patched method.
	/// </summary>
	/// <remarks>
	/// The chain must be installed first, since the head reads its state from the dispatch table.
	/// </remarks>
	public DynamicMethod Head { get; private set; } = null!;

	private DetourChain() {
	}

	/// <summary>
	/// Checks that a detour impl is valid for a given target, throwing if it isn't.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if the impl's signature does not match the target.
	/// </exception>
	public static void ValidateShape(DetourRegistration detour, MethodBase target) {
		InternalStateException.ThrowIfNull(detour);
		InternalStateException.ThrowIfNull(target);
		Shape.Validate(detour, returnTypeOf(target), parameterTypesOf(target));
	}

	/// <summary>
	/// Builds the chain for one method.
	/// </summary>
	/// <param name="target">The method being detoured, as the runtime sees it.</param>
	/// <param name="slot">The dispatch slot its prologue was emitted with.</param>
	/// <param name="detours">The detours in chain order.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if an impl's signature does not match the target.
	/// </exception>
	public static DetourChain Build(
		MethodBase target,
		int slot,
		ImmutableArray<DetourRegistration> detours
	) {
		InternalStateException.ThrowIfNull(target);
		if (detours.IsDefaultOrEmpty)
			throw new InternalStateException("can't make a detour chain out of zero detours");

		DetourChain chain = new();
		Type returnType = returnTypeOf(target);
		Type[] parameters = parameterTypesOf(target);

		var shapes = new Shape[detours.Length];
		for (int i = 0; i < detours.Length; i++)
			shapes[i] = Shape.Validate(detours[i], returnType, parameters);

		Delegate next = chain.buildTerminus(target, slot, returnType, parameters, shapes[^1]);
		for (int i = detours.Length - 1; i >= 1; i--)
			next = chain.buildLink(shapes[i], shapes[i - 1], next, returnType, parameters);

		chain.retained.Add(next);
		chain.State = new DetourLinkState {
			OwnerId = shapes[0].OwnerId,
			LocalId = shapes[0].LocalId,
			Next = next,
			Expected = parameters,
			Retained = chain.retained,
		};
		chain.Head = chain.buildHead(slot, shapes[0], returnType, parameters);
		chain.Entry = functionPointerOf(chain.Head);
		return chain;
	}

	// ==========================================================================================
	// thunks
	private DynamicMethod buildHead(int slot, Shape first, Type returnType, Type[] parameters) {
		DynamicMethod thunk = new(
			$"detour-head<0x{slot:x}>",
			returnType,
			parameters,
			typeof(DetourChain),
			skipVisibility: true
		);
		ILGenerator il = thunk.GetILGenerator();

		il.Emit(OpCodes.Ldc_I4, slot);
		il.Emit(OpCodes.Call, getChainStateMethod);
		il.Emit(OpCodes.Castclass, typeof(DetourLinkState));
		il.Emit(OpCodes.Ldfld, nextField);
		il.Emit(OpCodes.Castclass, first.NextType);

		for (int i = 0; i < parameters.Length; i++) {
			il.Emit(OpCodes.Ldarg, i);
			emitWiden(il, parameters[i], first.Parameters[i]);
		}

		il.Emit(OpCodes.Call, first.Impl);
		il.Emit(OpCodes.Ret);
		retained.Add(thunk);
		retained.Add(first.Impl);
		return thunk;
	}

	private Delegate buildLink(
		Shape shape,
		Shape caller,
		Delegate next,
		Type returnType,
		Type[] parameters
	) {
		DetourLinkState state = new() {
			OwnerId = caller.OwnerId,
			LocalId = caller.LocalId,
			Next = next,
			Expected = parameters,
		};

		DynamicMethod thunk = new(
			$"detour<{shape.OwnerId}::{shape.LocalId}>",
			returnType,
			[typeof(DetourLinkState), .. caller.Parameters],
			typeof(DetourChain),
			skipVisibility: true
		);
		ILGenerator il = thunk.GetILGenerator();

		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ldfld, nextField);
		il.Emit(OpCodes.Castclass, shape.NextType);

		for (int i = 0; i < parameters.Length; i++) {
			emitNarrowedArgument(il, caller.Parameters[i], parameters[i], i);
			emitWiden(il, parameters[i], shape.Parameters[i]);
		}

		il.Emit(OpCodes.Call, shape.Impl);
		il.Emit(OpCodes.Ret);

		Delegate link = thunk.CreateDelegate(caller.NextType, state);
		retained.Add(link);
		retained.Add(state);
		retained.Add(shape.Impl);
		return link;
	}

	private Delegate buildTerminus(
		MethodBase target,
		int slot,
		Type returnType,
		Type[] parameters,
		Shape caller
	) {
		// note: DetourReentrancyTests in the test project mirrors this pattern, if this changes
		// it must be updated accordingly
		DetourLinkState state = new() {
			OwnerId = caller.OwnerId,
			LocalId = caller.LocalId,
			Expected = parameters,
		};

		DynamicMethod thunk = new(
			$"detour-original<0x{slot:x}>",
			returnType,
			[typeof(DetourLinkState), .. caller.Parameters],
			typeof(DetourChain),
			skipVisibility: true
		);
		ILGenerator il = thunk.GetILGenerator();

		LocalBuilder depth = il.DeclareLocal(typeof(int));
		LocalBuilder? result = returnType == typeof(void) ? null : il.DeclareLocal(returnType);

		il.Emit(OpCodes.Ldc_I4, slot);
		il.Emit(OpCodes.Call, pushBypassMethod);
		il.Emit(OpCodes.Stloc, depth);

		il.BeginExceptionBlock();
		for (int i = 0; i < parameters.Length; i++)
			emitNarrowedArgument(il, caller.Parameters[i], parameters[i], i);
		il.Emit(OpCodes.Call, (MethodInfo)target);
		if (result is not null)
			il.Emit(OpCodes.Stloc, result);

		il.BeginFinallyBlock();
		il.Emit(OpCodes.Ldloc, depth);
		il.Emit(OpCodes.Call, unwindBypassMethod);
		il.EndExceptionBlock();

		if (result is not null)
			il.Emit(OpCodes.Ldloc, result);
		il.Emit(OpCodes.Ret);

		Delegate terminus = thunk.CreateDelegate(caller.NextType, state);
		retained.Add(terminus);
		retained.Add(state);
		return terminus;
	}

	// ==========================================================================================
	// conversions
	private static void emitWiden(ILGenerator il, Type from, Type to) {
		if (from != to && from.IsValueType)
			il.Emit(OpCodes.Box, from);
	}

	private static void emitNarrowedArgument(ILGenerator il, Type from, Type to, int index) {
		if (from == to) {
			il.Emit(OpCodes.Ldarg, index + 1);
			return;
		}
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ldarg, index + 1);
		il.Emit(OpCodes.Ldc_I4, index);
		il.Emit(OpCodes.Callvirt, narrowMethod);
		il.Emit(OpCodes.Unbox_Any, to);
	}

	// ==========================================================================================
	// method info
	private static Type[] parameterTypesOf(MethodBase target) {
		ParameterInfo[] declared = target.GetParameters();
		var types = new Type[declared.Length + (target.IsStatic ? 0 : 1)];
		int offset = 0;
		if (!target.IsStatic) {
			if (target.DeclaringType is null)
				throw new ArgumentException("an instance method must have a declaring type", nameof(target));
			types[offset++] = target.DeclaringType.IsValueType
				? target.DeclaringType.MakeByRefType()
				: target.DeclaringType;
		}
		foreach (ParameterInfo parameter in declared)
			types[offset++] = parameter.ParameterType;
		return types;
	}

	private static Type returnTypeOf(MethodBase target) =>
		target is MethodInfo method ? method.ReturnType : typeof(void);

	private static IntPtr functionPointerOf(DynamicMethod method) {
		RuntimeMethodHandle handle = DynamicMethodAccessor.GetMethodDescriptor(method);
		RuntimeHelpers.PrepareMethod(handle);
		return handle.GetFunctionPointer();
	}
}
