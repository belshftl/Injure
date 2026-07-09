// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.CodeAnalysis.Internal;

/// <summary>
/// Signifies that an object of the marked type, or collections of said type, should not be
/// stored in a field or property.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
internal sealed class DontCacheAttribute(string why) : Attribute {
	/// <summary>
	/// A human-readable description of why the object should not be stored in a field or property.
	/// </summary>
	public string Why { get; } = why;
}
