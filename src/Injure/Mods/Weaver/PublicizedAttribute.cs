// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace Injure.Mods.Weaver;

[AttributeUsage(
	AttributeTargets.Class |
	AttributeTargets.Struct |
	AttributeTargets.Enum |
	AttributeTargets.Constructor |
	AttributeTargets.Method |
	AttributeTargets.Property |
	AttributeTargets.Field |
	AttributeTargets.Event |
	AttributeTargets.Interface |
	AttributeTargets.Delegate,
	AllowMultiple = false,
	Inherited = false
)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PublicizedAttribute : Attribute;
