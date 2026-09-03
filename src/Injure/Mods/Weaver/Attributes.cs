// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace Injure.Mods.Weaver;

/// <summary>
/// Marks a generated delegate type as a modif target's identifier. <see cref="MetadataToken"/> is a
/// <c>MethodDef</c> token into the module that declares the target.
/// </summary>
[AttributeUsage(AttributeTargets.Delegate, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ModifTargetAttribute(int metadataToken) : Attribute {
	public int MetadataToken { get; } = metadataToken;
}

/// <summary>
/// Placed on the game assembly when the mirror tree is generated into it, both as a marker and to
/// carry information that makes removal possible without knowing the original options.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ModifInjectedAttribute(string rootSegment, string namingMode) : Attribute {
	public string RootSegment { get; } = rootSegment;
	public string NamingMode { get; } = namingMode;
}

/// <summary>
/// Placed on the publicized reference assembly; <c>[assembly: ReferenceAssembly]</c> is also placed
/// onto it, so this primarily just exists for consistency.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ModifPublicizedAttribute : Attribute;

/// <summary>
/// Marks a type/member as publicized. <see cref="OriginalVisibility"/> is the ECMA-335
/// <c>VisibilityMask</c> for types and <c>MemberAccessMask</c> for members.
/// </summary>
[AttributeUsage(
	AttributeTargets.Class
		| AttributeTargets.Struct
		| AttributeTargets.Interface
		| AttributeTargets.Enum
		| AttributeTargets.Delegate
		| AttributeTargets.Method
		| AttributeTargets.Constructor
		| AttributeTargets.Field,
	AllowMultiple = false
)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PublicizedAttribute(byte originalVisibility) : Attribute {
	public byte OriginalVisibility { get; } = originalVisibility;
}
