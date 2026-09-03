// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Provides helpers for creating structural IL references.
/// </summary>
public static class IlRefFactory {
	/// <summary>
	/// "Converts" a reflection type to a structural IL type reference. The "conversion" is irreversible.
	/// </summary>
	/// <param name="type">The reflection type.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="type"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// Thrown if <paramref name="type"/> is a function pointer type, as reflection function pointer conversion
	/// is not yet supported; compose it with <c>Signature(...)</c> and <c>FunctionPointer(...)</c>
	/// </exception>
	public static IlTypeRef Type(Type type) {
		ArgumentNullException.ThrowIfNull(type);
		if (type.IsFunctionPointer)
			throw new NotSupportedException("reflection function pointer conversion is not yet supported; compose it with Signature(...) and FunctionPointer(...)");
		if (type.IsByRef)
			return ByRef(Type(type.GetElementType()!));
		if (type.IsPointer)
			return Pointer(Type(type.GetElementType()!));
		if (type.IsArray) {
			IlTypeRef element = Type(type.GetElementType()!);
			return type.IsSZArray ? SzArray(element) : Array(element, type.GetArrayRank());
		}
		if (type.IsGenericParameter) {
			IlGenericParameterKind kind = type.DeclaringMethod is null ? IlGenericParameterKind.Type : IlGenericParameterKind.Method;
			return genericParameter(kind, type.GenericParameterPosition);
		}
		if (tryPrimitive(type, out PrimitiveTypeCode primitive))
			return new IlPrimitiveTypeRef(primitive);
		if (type.IsGenericType && !type.IsGenericTypeDefinition) {
			IlNamedTypeRef definition = requireNamedType(Type(type.GetGenericTypeDefinition()), nameof(type));
			return GenericInstance(definition, type.GetGenericArguments().Select(Type).ToArray());
		}
		AssemblyName assemblyName = type.Assembly.GetName();
		IlAssemblyIdentity assembly = IlAssemblyIdentityFactory.FromAssemblyName(assemblyName);
		IlTypeScope scope = new IlTypeScope.Assembly(assembly);
		Type? reflectionDeclaringType = type.DeclaringType;
		if (reflectionDeclaringType?.IsConstructedGenericType == true)
			reflectionDeclaringType = reflectionDeclaringType.GetGenericTypeDefinition();
		IlNamedTypeRef? declaring = reflectionDeclaringType is null ? null : requireNamedType(Type(reflectionDeclaringType), nameof(type));
		(string name, int arity) = splitMetadataName(type.Name);
		return new IlNamedTypeRef(
			scope,
			declaring,
			declaring is null ? type.Namespace ?? string.Empty : string.Empty,
			name,
			arity,
			type.IsValueType ? IlNamedTypeKind.ValueType : IlNamedTypeKind.Class
		);
	}

	/// <summary>
	/// Creates a target-type generic parameter (<c>!n</c>).
	/// </summary>
	/// <param name="index">
	/// The zero-based type generic parameter index across the target's enclosing and nested declaring types.
	/// </param>
	public static IlGenericParameterTypeRef TypeGenericParameter(int index) =>
		genericParameter(IlGenericParameterKind.Type, index);

	/// <summary>
	/// Creates a target-method generic parameter (<c>!!n</c>).
	/// </summary>
	/// <param name="index">The zero-based method generic parameter index.</param>
	public static IlGenericParameterTypeRef MethodGenericParameter(int index) =>
		genericParameter(IlGenericParameterKind.Method, index);

	/// <summary>
	/// Creates an instantiated generic type.
	/// </summary>
	/// <param name="genericType">The named generic type definition.</param>
	/// <param name="arguments">The generic arguments, in order.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="genericType"/> or <paramref name="arguments"/> is <see langword="null"/>.
	/// </exception>
	public static IlGenericInstanceTypeRef GenericInstance(IlNamedTypeRef genericType, params IlTypeRef[] arguments) {
		ArgumentNullException.ThrowIfNull(arguments);
		return GenericInstance(genericType, arguments.AsSpan());
	}

	/// <summary>
	/// Creates an instantiated generic type.
	/// </summary>
	/// <param name="genericType">The named generic type definition.</param>
	/// <param name="arguments">The generic arguments, in order.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="genericType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="genericType"/> does not declare or inherit generic parameters, or if
	/// it expects a different amount of generic arguments from the supplied amount.
	/// </exception>
	public static IlGenericInstanceTypeRef GenericInstance(IlNamedTypeRef genericType, ReadOnlySpan<IlTypeRef> arguments) {
		ArgumentNullException.ThrowIfNull(genericType);
		int expected = GetTypeGenericParameterCount(genericType);
		if (expected == 0)
			throw new ArgumentException("the named type does not declare or inherit generic parameters", nameof(genericType));
		if (arguments.Length != expected)
			throw new ArgumentException($"the generic type expects {expected} argument(s), but {arguments.Length} were supplied", nameof(arguments));
		return new IlGenericInstanceTypeRef(genericType, copyAndValidateTypeArguments(arguments, nameof(arguments)));
	}

	/// <summary>
	/// Creates an szarray (single-dimension, zero-indexed array) type.
	/// </summary>
	/// <param name="elementType">The array element type.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="elementType"/> is <see langword="null"/>.
	/// </exception>
	public static IlSzArrayTypeRef SzArray(IlTypeRef elementType) {
		validateArrayElementType(elementType, nameof(elementType));
		return new IlSzArrayTypeRef(elementType);
	}

	/// <summary>
	/// Creates a higher-rank array type.
	/// </summary>
	/// <param name="elementType">The array element type.</param>
	/// <param name="rank">The positive array rank.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="elementType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="rank"/> is negative or zero.
	/// </exception>
	public static IlArrayTypeRef Array(IlTypeRef elementType, int rank) =>
		Array(elementType, rank, ReadOnlySpan<int>.Empty, ReadOnlySpan<int>.Empty);

	/// <summary>
	/// Creates a non-szarray array type (higher rank, non-zero lower bound, etc.)
	/// </summary>
	/// <param name="elementType">The array element type.</param>
	/// <param name="rank">The positive array rank.</param>
	/// <param name="sizes">The specified leading dimension sizes.</param>
	/// <param name="lowerBounds">The specified leading lower bounds.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="elementType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="rank"/> is negative or zero, or if <paramref name="sizes"/> contains
	/// a negative size.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the number of specified sizes / lower bounds exceeds the array rank.
	/// </exception>
	public static IlArrayTypeRef Array(IlTypeRef elementType, int rank, ReadOnlySpan<int> sizes, ReadOnlySpan<int> lowerBounds) {
		validateArrayElementType(elementType, nameof(elementType));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rank);
		if (sizes.Length > rank)
			throw new ArgumentException("the number of specified sizes must not exceed the array rank", nameof(sizes));
		if (lowerBounds.Length > rank)
			throw new ArgumentException("the number of specified lower bounds must not exceed the array rank", nameof(lowerBounds));
		foreach (int size in sizes)
			if (size < 0)
				throw new ArgumentOutOfRangeException(nameof(sizes), "array sizes must be non-negative");
		return new IlArrayTypeRef(elementType, rank, sizes.ToImmutableArray(), lowerBounds.ToImmutableArray());
	}

	/// <summary>
	/// Creates an unmanaged pointer type.
	/// </summary>
	/// <param name="elementType">The pointee type.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="elementType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="elementType"/> is a byref or pinned type.
	/// </exception>
	public static IlPointerTypeRef Pointer(IlTypeRef elementType) {
		ArgumentNullException.ThrowIfNull(elementType);
		if (elementType is IlByRefTypeRef or IlPinnedTypeRef)
			throw new ArgumentException("a pointer element cannot be byref or pinned", nameof(elementType));
		return new IlPointerTypeRef(elementType);
	}

	/// <summary>
	/// Creates a managed byref type.
	/// </summary>
	/// <param name="elementType">The referenced type.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="elementType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="elementType"/> is <c>void</c>, byref, or pinned.
	/// </exception>
	public static IlByRefTypeRef ByRef(IlTypeRef elementType) {
		ArgumentNullException.ThrowIfNull(elementType);
		if (elementType.IsVoid || elementType is IlByRefTypeRef or IlPinnedTypeRef)
			throw new ArgumentException("a byref element cannot be void, byref, or pinned", nameof(elementType));
		return new IlByRefTypeRef(elementType);
	}

	/// <summary>
	/// Applies a required custom modifier.
	/// </summary>
	/// <param name="modifier">The modifier type.</param>
	/// <param name="unmodifiedType">The type to modify.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="modifier"/> or <paramref name="unmodifiedType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="unmodifiedType"/> is pinned.
	/// </exception>
	public static IlModifiedTypeRef RequiredModifier(IlTypeRef modifier, IlTypeRef unmodifiedType) =>
		modifiedType(modifier, unmodifiedType, true);

	/// <summary>
	/// Applies an optional custom modifier.
	/// </summary>
	/// <param name="modifier">The modifier type.</param>
	/// <param name="unmodifiedType">The type to modify.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="modifier"/> or <paramref name="unmodifiedType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="unmodifiedType"/> is pinned.
	/// </exception>
	public static IlModifiedTypeRef OptionalModifier(IlTypeRef modifier, IlTypeRef unmodifiedType) =>
		modifiedType(modifier, unmodifiedType, false);

	/// <summary>
	/// Creates a function pointer type.
	/// </summary>
	/// <param name="signature">The function pointer signature.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="signature"/> is <see langword="null"/>.
	/// </exception>
	public static IlFunctionPointerTypeRef FunctionPointer(IlMethodSignature signature) =>
		new(signature ?? throw new ArgumentNullException(nameof(signature)));

	/// <summary>
	/// Creates a managed, non-generic method signature.
	/// </summary>
	/// <param name="returnType">The return type.</param>
	/// <param name="parameterTypes">The parameter types, in order.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="returnType"/> or <paramref name="parameterTypes"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Convenience overload for <see cref="Signature(SignatureCallingConvention, bool, bool, int, int, IlTypeRef, ReadOnlySpan{IlTypeRef})"/>.
	/// </remarks>
	public static IlMethodSignature Signature(IlTypeRef returnType, params IlTypeRef[] parameterTypes) {
		ArgumentNullException.ThrowIfNull(parameterTypes);
		return Signature(
			SignatureCallingConvention.Default,
			false,
			false,
			0,
			parameterTypes.Length,
			returnType,
			parameterTypes
		);
	}

	/// <summary>
	/// Creates a managed, non-generic or generic method signature.
	/// </summary>
	/// <param name="returnType">The return type.</param>
	/// <param name="genericParameterCount">The number of method generic parameters.</param>
	/// <param name="parameterTypes">The parameter types, in order.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="returnType"/> or <paramref name="parameterTypes"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Convenience overload for <see cref="Signature(SignatureCallingConvention, bool, bool, int, int, IlTypeRef, ReadOnlySpan{IlTypeRef})"/>.
	/// </remarks>
	public static IlMethodSignature Signature(IlTypeRef returnType, int genericParameterCount, params IlTypeRef[] parameterTypes) {
		ArgumentNullException.ThrowIfNull(parameterTypes);
		return Signature(
			SignatureCallingConvention.Default,
			false,
			false,
			genericParameterCount,
			parameterTypes.Length,
			returnType,
			parameterTypes
		);
	}

	/// <summary>
	/// Creates a method or standalone callsite signature.
	/// </summary>
	/// <param name="callingConvention">The managed or unmanaged calling convention.</param>
	/// <param name="hasThis">Whether the signature has an implicit instance receiver.</param>
	/// <param name="explicitThis">Whether the instance receiver is explicitly represented.</param>
	/// <param name="genericParameterCount">The number of method generic parameters.</param>
	/// <param name="requiredParameterCount">The number of required parameters before optional varargs parameters.</param>
	/// <param name="returnType">The return type.</param>
	/// <param name="parameterTypes">The parameter types, in order.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="returnType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="genericParameterCount"/> is negative or if <paramref name="requiredParameterCount"/>
	/// is greater than the length of <paramref name="parameterTypes"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="explicitThis"/> is <see langword="true"/> but <paramref name="hasThis"/> is not.
	/// </exception>
	public static IlMethodSignature Signature(
		SignatureCallingConvention callingConvention,
		bool hasThis,
		bool explicitThis,
		int genericParameterCount,
		int requiredParameterCount,
		IlTypeRef returnType,
		ReadOnlySpan<IlTypeRef> parameterTypes
	) {
		ArgumentNullException.ThrowIfNull(returnType);
		ArgumentOutOfRangeException.ThrowIfNegative(genericParameterCount);
		if ((uint)requiredParameterCount > (uint)parameterTypes.Length)
			throw new ArgumentOutOfRangeException(nameof(requiredParameterCount));
		if (explicitThis && !hasThis)
			throw new ArgumentException("explicit-this signatures must also have an instance receiver", nameof(explicitThis));
		ImmutableArray<IlTypeRef> parameters = copyAndValidateParameterTypes(parameterTypes, nameof(parameterTypes));
		return new IlMethodSignature(
			callingConvention,
			hasThis,
			explicitThis,
			genericParameterCount,
			requiredParameterCount,
			returnType,
			parameters
		);
	}

	/// <summary>
	/// Creates a generic method instantiation.
	/// </summary>
	/// <param name="genericMethod">The uninstantiated generic method reference.</param>
	/// <param name="arguments">The generic method arguments, in order.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="genericMethod"/> or <paramref name="arguments"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="genericMethod"/> is already a generic instantiation, not generic, or
	/// expects a different amount of generic arguments from the supplied amount.
	/// </exception>
	public static IlMethodRef GenericMethod(IlMethodRef genericMethod, params IlTypeRef[] arguments) {
		ArgumentNullException.ThrowIfNull(arguments);
		return GenericMethod(genericMethod, arguments.AsSpan());
	}

	/// <summary>
	/// Creates a generic method instantiation.
	/// </summary>
	/// <param name="genericMethod">The uninstantiated generic method reference.</param>
	/// <param name="arguments">The generic method arguments, in order.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="genericMethod"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="genericMethod"/> is already a generic instantiation, not generic, or
	/// expects a different amount of generic arguments from the supplied amount.
	/// </exception>
	public static IlMethodRef GenericMethod(IlMethodRef genericMethod, ReadOnlySpan<IlTypeRef> arguments) {
		ArgumentNullException.ThrowIfNull(genericMethod);
		if (genericMethod.IsGenericInstantiation)
			throw new ArgumentException("the supplied method is already a generic instantiation", nameof(genericMethod));
		int expected = genericMethod.Signature.GenericParameterCount;
		if (expected == 0)
			throw new ArgumentException("the supplied method is not generic", nameof(genericMethod));
		if (arguments.Length != expected)
			throw new ArgumentException($"the generic method expects {expected} argument(s), but {arguments.Length} were supplied", nameof(arguments));
		return new IlMethodRef(
			genericMethod.DeclaringType,
			genericMethod.Name,
			genericMethod.Signature,
			copyAndValidateTypeArguments(arguments, nameof(arguments))
		);
	}

	/// <summary>
	/// "Converts" a reflection method or constructor to a structural IL method reference.
	/// The "conversion" is irreversible.
	/// </summary>
	/// <param name="method">The reflection method or constructor.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="method"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="method"/> has no declaring type.
	/// </exception>
	public static IlMethodRef Method(MethodBase method) {
		ArgumentNullException.ThrowIfNull(method);
		Type declaringType = method.DeclaringType ?? throw new ArgumentException("method has no declaring type", nameof(method));
		MethodBase signatureSource = getOpenDeclaringTypeMember(method, declaringType);
		if (signatureSource is MethodInfo { IsGenericMethod: true, IsGenericMethodDefinition: false } genericMethod)
			signatureSource = genericMethod.GetGenericMethodDefinition();

		IlTypeRef returnType = signatureSource is MethodInfo methodInfo
			? Type(methodInfo.ReturnType)
			: new IlPrimitiveTypeRef(PrimitiveTypeCode.Void);
		ParameterInfo[] parameters = signatureSource.GetParameters();
		CallingConventions reflectionConvention = signatureSource.CallingConvention;
		bool varargs = (reflectionConvention & CallingConventions.VarArgs) != 0;
		bool hasThis = (reflectionConvention & CallingConventions.HasThis) != 0;
		bool explicitThis = (reflectionConvention & CallingConventions.ExplicitThis) != 0;
		int genericParameterCount = signatureSource.IsGenericMethod
			? signatureSource.GetGenericArguments().Length
			: 0;
		ImmutableArray<IlTypeRef> genericArguments = method.IsGenericMethod && !method.IsGenericMethodDefinition
			? method.GetGenericArguments().Select(Type).ToImmutableArray()
			: [];

		IlMethodSignature signature = Signature(
			varargs ? SignatureCallingConvention.VarArgs : SignatureCallingConvention.Default,
			hasThis,
			explicitThis,
			genericParameterCount,
			parameters.Length,
			returnType,
			parameters.Select(static parameter => parameter.ParameterType).Select(Type).ToArray()
		);
		return new IlMethodRef(Type(declaringType), method.Name, signature, genericArguments);
	}

	/// <summary>
	/// Creates a method reference from its declaring type, name, and signature.
	/// </summary>
	/// <param name="declaringType">The declaring type of the method.</param>
	/// <param name="name">The name of the method.</param>
	/// <param name="signature">The method's signature, written in the declaring type's parameter space.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="declaringType"/>, <paramref name="name"/>, or <paramref name="signature"/> is empty.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="declaringType"/> cannot declare members or if <paramref name="name"/> is empty.
	/// </exception>
	/// <remarks>
	/// <para>
	/// This is an advanced overload. Most of the time, <see cref="Method(MethodBase)"/> is sufficient.
	/// </para>
	/// <para>
	/// This is the only way to name a method whose declaring type can't be expressed as a
	/// <see cref="MethodBase"/>, which is most importantly a method on a generic instantiation whose
	/// arguments are the patched method's own generic parameters, such as
	/// <c>Outer&lt;!!0&gt;.Inner&lt;!!1&gt;::Method&lt;!!2&gt;</c>. Reflection has no handle for those, so
	/// <see cref="Method(MethodBase)"/> isn't usable.
	/// </para>
	/// <para>
	/// <paramref name="signature"/> is the method's definition signature, written in the declaring
	/// type's own parameter space: the parameters of <c>Method</c> above are <c>!0</c>, <c>!1</c>, and
	/// <c>!!0</c>, not the call site's <c>!!0</c>, <c>!!1</c>, <c>!!2</c>. Only
	/// <paramref name="declaringType"/> and the arguments later passed to
	/// <see cref="GenericMethod(IlMethodRef, IlTypeRef[])"/> are written in the call
	/// site's space. Mixing the two produces a reference that resolves to a different row, or to none.
	/// </para>
	/// <para>
	/// The result is never a generic instantiation; if you want one, pass the generic definition signature
	/// here and instantiate the result with <see cref="GenericMethod(IlMethodRef, IlTypeRef[])"/>.
	/// </para>
	/// </remarks>
	public static IlMethodRef Method(IlTypeRef declaringType, string name, IlMethodSignature signature) {
		ArgumentNullException.ThrowIfNull(declaringType);
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(signature);
		if (!canDeclareMembers(declaringType))
			throw new ArgumentException($"{declaringType} cannot declare methods", nameof(declaringType));
		return new IlMethodRef(declaringType, name, signature, []);
	}

	/// <summary>
	/// "Converts" a reflection field to a structural IL field reference.
	/// The "conversion" is irreversible.
	/// </summary>
	/// <param name="field">The reflection field.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="field"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="field"/> has no declaring type.
	/// </exception>
	public static IlFieldRef Field(FieldInfo field) {
		ArgumentNullException.ThrowIfNull(field);
		Type declaringType = field.DeclaringType ?? throw new ArgumentException("field has no declaring type", nameof(field));
		FieldInfo signatureSource = getOpenDeclaringTypeMember(field, declaringType);
		return new IlFieldRef(Type(declaringType), field.Name, Type(signatureSource.FieldType));
	}

	/// <summary>
	/// Creates a field reference from its declaring type, name, and field type.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is an advanced overload. Most of the time, <see cref="Field(FieldInfo)"/> is sufficient.
	/// </para>
	/// <para>
	/// The field counterpart to <see cref="Method(IlTypeRef, string, IlMethodSignature)"/>. See its docs
	/// for why this is necessary; in short, <c>ldfld !0 Box&lt;!!0&gt;::Value</c> names a field on an
	/// instantiation that you can't express with reflection.
	/// </para>
	/// </remarks>
	public static IlFieldRef Field(IlTypeRef declaringType, string name, IlTypeRef fieldType) {
		ArgumentNullException.ThrowIfNull(declaringType);
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(fieldType);
		if (!canDeclareMembers(declaringType))
			throw new ArgumentException($"{declaringType} cannot declare fields", nameof(declaringType));
		return new IlFieldRef(declaringType, name, fieldType);
	}

	/// <summary>
	/// Whether a type can appear as the declaring type of a member reference.
	/// </summary>
	/// <remarks>
	/// The rule is that the factory accepts everything the decoder can produce. Primitives qualify
	/// because certain corelib types are normalized to <see cref="IlPrimitiveTypeRef"/>, so
	/// <c>System.String::get_Length</c> has a primitive declaring type; array types qualify because of
	/// the <c>Get</c>, <c>Set</c>, <c>Address</c>, and <c>.ctor</c> pseudo-methods. Byrefs, pointers,
	/// function pointers, and modified types declare nothing.
	/// </remarks>
	private static bool canDeclareMembers(IlTypeRef type) => type is
		IlNamedTypeRef or IlGenericInstanceTypeRef or IlPrimitiveTypeRef or
		IlSzArrayTypeRef or IlArrayTypeRef or IlGenericParameterTypeRef or IlGlobalModuleTypeRef;

	internal static int GetTypeGenericParameterCount(IlTypeRef type) => type switch {
		IlGenericInstanceTypeRef instance => instance.Arguments.Length,
		IlNamedTypeRef named => checked((named.DeclaringType is null ? 0 : GetTypeGenericParameterCount(named.DeclaringType)) + named.GenericArity),
		IlModifiedTypeRef modified => GetTypeGenericParameterCount(modified.UnmodifiedType),
		_ => 0,
	};

	private static IlGenericParameterTypeRef genericParameter(IlGenericParameterKind kind, int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new IlGenericParameterTypeRef(kind, index);
	}

	private static IlModifiedTypeRef modifiedType(IlTypeRef modifier, IlTypeRef unmodifiedType, bool isRequired) {
		ArgumentNullException.ThrowIfNull(modifier);
		ArgumentNullException.ThrowIfNull(unmodifiedType);
		if (unmodifiedType is IlPinnedTypeRef)
			throw new ArgumentException("pinned type cannot be custom-modified", nameof(unmodifiedType));
		return new IlModifiedTypeRef(modifier, unmodifiedType, isRequired);
	}

	private static ImmutableArray<IlTypeRef> copyAndValidateTypeArguments(ReadOnlySpan<IlTypeRef> arguments, string paramName) {
		ImmutableArray<IlTypeRef>.Builder builder = ImmutableArray.CreateBuilder<IlTypeRef>(arguments.Length);
		foreach (IlTypeRef? argument in arguments) {
			if (argument is null)
				throw new ArgumentException("generic arguments must not contain null", paramName);
			if (argument.IsVoid || argument is IlByRefTypeRef or IlPinnedTypeRef)
				throw new ArgumentException("generic arguments cannot be void, byref, or pinned", paramName);
			builder.Add(argument);
		}
		return builder.MoveToImmutable();
	}

	private static ImmutableArray<IlTypeRef> copyAndValidateParameterTypes(ReadOnlySpan<IlTypeRef> parameterTypes, string paramName) {
		ImmutableArray<IlTypeRef>.Builder builder = ImmutableArray.CreateBuilder<IlTypeRef>(parameterTypes.Length);
		foreach (IlTypeRef? parameterType in parameterTypes) {
			if (parameterType is null)
				throw new ArgumentException("parameter types must not contain null", paramName);
			if (parameterType.IsVoid || parameterType is IlPinnedTypeRef)
				throw new ArgumentException("parameter types cannot be void or pinned", paramName);
			builder.Add(parameterType);
		}
		return builder.MoveToImmutable();
	}

	private static void validateArrayElementType(IlTypeRef elementType, string paramName) {
		ArgumentNullException.ThrowIfNull(elementType, paramName);
		if (elementType.IsVoid || elementType is IlByRefTypeRef or IlPinnedTypeRef)
			throw new ArgumentException("an array element cannot be void, byref, or pinned", paramName);
	}

	private static IlNamedTypeRef requireNamedType(IlTypeRef type, string paramName) => type as IlNamedTypeRef ??
		throw new ArgumentException("the type must resolve to a named type definition", paramName);

	private static MethodBase getOpenDeclaringTypeMember(MethodBase method, Type declaringType) {
		if (!declaringType.IsConstructedGenericType)
			return method;
		Type definition = declaringType.GetGenericTypeDefinition();
		foreach (MemberInfo member in definition.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
			if (member is MethodBase candidate && candidate.MetadataToken == method.MetadataToken)
				return candidate;
		throw new ArgumentException("could not resolve method on its open generic declaring type", nameof(method));
	}

	private static FieldInfo getOpenDeclaringTypeMember(FieldInfo field, Type declaringType) {
		if (!declaringType.IsConstructedGenericType)
			return field;
		Type definition = declaringType.GetGenericTypeDefinition();
		foreach (FieldInfo candidate in definition.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
			if (candidate.MetadataToken == field.MetadataToken)
				return candidate;
		throw new ArgumentException("could not resolve field on its open generic declaring type", nameof(field));
	}

	private static (string Name, int Arity) splitMetadataName(string name) {
		int bt = name.LastIndexOf('`');
		if (bt < 0 || !int.TryParse(name.AsSpan(bt + 1), out int arity))
			return (name, 0);
		return (name[..bt], arity);
	}

	private static bool tryPrimitive(Type type, out PrimitiveTypeCode code) {
		code = default;
		if (type == typeof(void)) code = PrimitiveTypeCode.Void;
		else if (type == typeof(bool)) code = PrimitiveTypeCode.Boolean;
		else if (type == typeof(char)) code = PrimitiveTypeCode.Char;
		else if (type == typeof(sbyte)) code = PrimitiveTypeCode.SByte;
		else if (type == typeof(byte)) code = PrimitiveTypeCode.Byte;
		else if (type == typeof(short)) code = PrimitiveTypeCode.Int16;
		else if (type == typeof(ushort)) code = PrimitiveTypeCode.UInt16;
		else if (type == typeof(int)) code = PrimitiveTypeCode.Int32;
		else if (type == typeof(uint)) code = PrimitiveTypeCode.UInt32;
		else if (type == typeof(long)) code = PrimitiveTypeCode.Int64;
		else if (type == typeof(ulong)) code = PrimitiveTypeCode.UInt64;
		else if (type == typeof(float)) code = PrimitiveTypeCode.Single;
		else if (type == typeof(double)) code = PrimitiveTypeCode.Double;
		else if (type == typeof(string)) code = PrimitiveTypeCode.String;
		else if (type == typeof(TypedReference)) code = PrimitiveTypeCode.TypedReference;
		else if (type == typeof(IntPtr)) code = PrimitiveTypeCode.IntPtr;
		else if (type == typeof(UIntPtr)) code = PrimitiveTypeCode.UIntPtr;
		else if (type == typeof(object)) code = PrimitiveTypeCode.Object;
		else return false;
		return true;
	}
}
