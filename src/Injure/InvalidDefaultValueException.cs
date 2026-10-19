// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Injure;

/// <summary>
/// Exception thrown on usage of the default value of a struct whose default value is invalid.
/// </summary>
/// <remarks>
/// See <see cref="IHasInvalidDefault"/>.
/// </remarks>
public sealed class InvalidDefaultException(string message) : Exception(message) {
	internal static string MessageFor(string typeName) => $"the default value of the struct '{typeName}' is invalid";

	/// <summary>
	/// Constructs a new <see cref="InvalidDefaultException"/> stating that the default value of
	/// the struct named <paramref name="typeName"/> is invalid.
	/// </summary>
	public static InvalidDefaultException For(string typeName) => new(MessageFor(typeName));

	/// <summary>
	/// Constructs a new <see cref="InvalidDefaultException"/> stating that the default value of
	/// the struct <typeparamref name="T"/> is invalid.
	/// </summary>
	public static InvalidDefaultException For<T>() where T : struct, allows ref struct =>
		For(typeof(T).ToString());
}

/// <summary>
/// <see cref="ArgumentException"/> derived class thrown for inputs that are the default value of a
/// struct whose default value is invalid.
/// </summary>
/// <remarks>
/// See <see cref="IHasInvalidDefault"/>.
/// </remarks>
public sealed class ArgumentInvalidDefaultException : ArgumentException {
	/// <summary>
	/// Constructs a new <see cref="ArgumentInvalidDefaultException"/> with the given message.
	/// </summary>
	public ArgumentInvalidDefaultException(string message) : base(message) {}

	/// <summary>
	/// Constructs a new <see cref="ArgumentInvalidDefaultException"/> with the given message and
	/// <paramref name="paramName"/>.
	/// </summary>
	public ArgumentInvalidDefaultException(string message, string? paramName)
		: base(message, paramName) {}

	/// <summary>
	/// Constructs a new <see cref="ArgumentInvalidDefaultException"/> stating that the default value
	/// of the struct <typeparamref name="T"/> is invalid.
	/// </summary>
	public static ArgumentInvalidDefaultException For<T>() where T : struct, allows ref struct =>
		new(InvalidDefaultException.MessageFor(typeof(T).ToString()));

	/// <summary>
	/// Constructs a new <see cref="ArgumentInvalidDefaultException"/> stating that the default value
	/// of the struct <typeparamref name="T"/> is invalid, with the given <paramref name="paramName"/>.
	/// </summary>
	public static ArgumentInvalidDefaultException For<T>(string? paramName)
		where T : struct, allows ref struct =>
		new(InvalidDefaultException.MessageFor(typeof(T).ToString()), paramName);

	/// <summary>
	/// Throws an <see cref="ArgumentInvalidDefaultException"/> if <paramref name="value"/> is the
	/// default value of <typeparamref name="T"/>.
	/// </summary>
	/// <param name="value">The argument to check.</param>
	/// <param name="paramName">
	/// The name of the parameter <paramref name="value"/> was passed as; the argument expression
	/// unless set.
	/// </param>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="value"/> is the default value of <typeparamref name="T"/>.
	/// </exception>
	public static void ThrowIfDefault<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
		where T : struct, IHasInvalidDefault, allows ref struct {
		if (value.IsDefault)
			throwFor<T>(paramName);
	}

	/// <summary>
	/// Throws an <see cref="ArgumentInvalidDefaultException"/> if <paramref name="value"/> isn't
	/// <see langword="null"/> but is the default value of <typeparamref name="T"/>.
	/// </summary>
	/// <param name="value">The argument to check.</param>
	/// <param name="paramName">
	/// The name of the parameter <paramref name="value"/> was passed as; the argument expression
	/// unless set.
	/// </param>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="value"/> is the default value of <typeparamref name="T"/>.
	/// </exception>
	public static void ThrowIfDefault<T>(T? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
		where T : struct, IHasInvalidDefault {
		if (value is T v && v.IsDefault)
			throwFor<T>(paramName);
	}

	// checks every element, naming the first default one as `paramName[index]`
	internal static void ThrowIfAnyDefault<T>(ReadOnlySpan<T> values, string paramName) where T : struct, IHasInvalidDefault {
		for (int i = 0; i < values.Length; i++)
			if (values[i].IsDefault)
				throwFor<T>($"{paramName}[{i}]");
	}

	[DoesNotReturn]
	private static void throwFor<T>(string? paramName) where T : struct, allows ref struct =>
		throw new ArgumentInvalidDefaultException(InvalidDefaultException.MessageFor(typeof(T).ToString()), paramName);
}
