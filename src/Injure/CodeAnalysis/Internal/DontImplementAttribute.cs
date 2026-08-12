// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.CodeAnalysis.Internal;

/// <summary>
/// Marks an interface that exists specifically for the purpose of being exposed as an API
/// surface and must not be implemented outside of the engine (or is outright impossible to
/// correctly implement outside of the engine).
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
internal sealed class DontImplementAttribute : Attribute;
