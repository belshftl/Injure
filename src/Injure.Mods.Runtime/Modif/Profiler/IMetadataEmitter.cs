// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// Emitter for metadata rows into a target module.
/// </summary>
/// <remarks>
/// Metadata is append-only; <c>IMetaDataEmit2</c> has no removal counterpart, and every defined
/// row lives for the process's lifetime, which is why the resolver caches aggressively and why a
/// reload must reuse rows rather than define new ones.
/// </remarks>
internal interface IMetadataEmitter {
	/// <summary>
	/// Defines or returns an <c>AssemblyRef</c> row.
	/// </summary>
	int DefineAssemblyReference(IlAssemblyIdentity identity);

	/// <summary>
	/// Defines or returns a <c>TypeRef</c> row.
	/// </summary>
	/// <param name="resolutionScope">An <c>AssemblyRef</c>, <c>ModuleRef</c>, or enclosing <c>TypeRef</c> token.</param>
	/// <param name="namespace">Type namespace.</param>
	/// <param name="name">Type name.</param>
	int DefineTypeReference(int resolutionScope, string @namespace, string name);

	/// <summary>
	/// Defines or returns a <c>TypeSpec</c> row from an encoded signature blob.
	/// </summary>
	int DefineTypeSpecification(ReadOnlySpan<byte> signature);

	/// <summary>
	/// Defines or returns a <c>MemberRef</c> row.
	/// </summary>
	/// <param name="parent">A <c>TypeRef</c>, <c>TypeSpec</c>, <c>ModuleRef</c>, or <c>MethodDef</c> token.</param>
	/// <param name="name">Member name.</param>
	/// <param name="signature">Encoded signature blob.</param>
	int DefineMemberReference(int parent, string name, ReadOnlySpan<byte> signature);

	/// <summary>
	/// Defines or returns a <c>MethodSpec</c> row.
	/// </summary>
	int DefineMethodSpecification(int method, ReadOnlySpan<byte> signature);

	/// <summary>
	/// Defines or returns a <c>StandAloneSig</c> row.
	/// </summary>
	int DefineStandaloneSignature(ReadOnlySpan<byte> signature);

	/// <summary>
	/// Defines or returns a user string heap entry, returning its token (prefixed with <c>0x70</c>).
	/// </summary>
	int DefineUserString(string value);

	/// <summary>
	/// Commits every row defined since the last commit.
	/// </summary>
	/// <remarks>
	/// Tokens returned above are usable in IL only after this returns.
	/// </remarks>
	void Commit();
}
