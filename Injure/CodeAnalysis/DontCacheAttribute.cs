// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.CodeAnalysis;

/// <summary>
/// Signifies that an object of the marked type should not be stored in a field or property.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class DontCacheAttribute : Attribute {
	internal DontCacheAttribute() {}
}
