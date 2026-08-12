// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.CodeAnalysis.Internal;

/// <summary>
/// Signifies that an object of the marked type, or collections of said type, should not be
/// captured into a lambda or local function.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
internal sealed class DontCaptureIntoClosureAttribute(string why) : Attribute {
	/// <summary>
	/// A human-readable description of why the object should not be captured into a lambda or local function.
	/// </summary>
	public string Why { get; } = why;
}
