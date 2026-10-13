// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure;

/// <summary>
/// Exception thrown on usage of the default value of a struct whose default value is invalid.
/// </summary>
public sealed class InvalidDefaultException(string message) : Exception(message) {
	/// <summary>
	/// Constructs a new <see cref="InvalidDefaultException"/> stating that the default value of
	/// the struct named <paramref name="typeName"/> is invalid.
	/// </summary>
	public static InvalidDefaultException For(string typeName) =>
		new($"the default value of the struct '{typeName}' is invalid");

	/// <summary>
	/// Constructs a new <see cref="InvalidDefaultException"/> stating that the default value of
	/// the struct <typeparamref name="T"/> is invalid.
	/// </summary>
	public static InvalidDefaultException For<T>() where T : struct, allows ref struct =>
		new(typeof(T).ToString());
}

/// <summary>
/// <see cref="ArgumentException"/> derived class thrown for inputs that are the default value of a
/// struct whose default value is invalid.
/// </summary>
public sealed class ArgumentInvalidDefaultException : ArgumentException {
	/// <summary>
	/// Constructs a new <see cref="ArgumentInvalidDefaultException"/> with the given message.
	/// </summary>
	public ArgumentInvalidDefaultException(string message) : base(message) {}

	/// <summary>
	/// Constructs a new <see cref="ArgumentInvalidDefaultException"/> with the given message and
	/// <paramref name="paramName"/>.
	/// </summary>
	public ArgumentInvalidDefaultException(string message, string paramName)
		: base(message, paramName) {}

	/// <summary>
	/// Constructs a new <see cref="ArgumentInvalidDefaultException"/> stating that the default value
	/// of the struct <typeparamref name="T"/> is invalid.
	/// </summary>
	public static ArgumentInvalidDefaultException For<T>() where T : struct, allows ref struct =>
		new(typeof(T).ToString());

	/// <summary>
	/// Constructs a new <see cref="ArgumentInvalidDefaultException"/> stating that the default value
	/// of the struct <typeparamref name="T"/> is invalid, with the given <paramref name="paramName"/>.
	/// </summary>
	public static ArgumentInvalidDefaultException For<T>(string paramName)
		where T : struct, allows ref struct =>
		new(typeof(T).ToString(), paramName);
}
