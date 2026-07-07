// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods;

/// <summary>
/// Marker interface that marks a type as "holds risky / potentially ALC-rooting strong references"
/// and allows dropping said references.
/// </summary>
/// <remarks>
/// <para>
/// A good implementation recursively calls <see cref="DropStrongReferences()"/> on its children if
/// applicable, clears held collections / arrays, nulls all sensitive-type fields, and makes the object
/// unusable (subject to common-sense "fine to retain" exceptions like strings). On post-drop usage attempts,
/// generally throw; remember to only throw <see cref="InternalStateException"/> if you're sure that
/// user code or a public API path can't trip it, since it's a "bug in the library" exception. Not
/// throwing can be fine in some cases such as the object being a DTO or other lightweight/storage type.
/// </para>
/// <para>
/// You're probably doing something wrong if you have a struct implementing this.
/// </para>
/// </remarks>
internal interface IStrongRefDroppable {
	/// <summary>
	/// Drops the strong references that this object holds, typically making it unusable/invalid.
	/// </summary>
	/// <remarks>
	/// Ideally, this would be "drops every strong reference that this object holds", but that
	/// promises quite a lot and plenty of basic types like strings are fine to retain.
	/// </remarks>
	void DropStrongReferences();
}
