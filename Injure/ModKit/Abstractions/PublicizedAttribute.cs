// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

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
public sealed class PublicizedAttribute : Attribute {
}
