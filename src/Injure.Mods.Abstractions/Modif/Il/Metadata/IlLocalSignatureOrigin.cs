// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Injure.Mods.Abstractions.Modif.Il.Metadata;

/// <summary>
/// An optimization hint identifying the metadata standalone signature a body's locals were decoded from.
/// </summary>
/// <remarks>
/// <para>
/// A resolver may use this to skip re-encoding a locals signature if the hint names the module
/// it's emitting into and the locals list is unchanged. Never part of equality, and must never
/// change the resolved result in a correct resolver. A resolver can ignore this entirely and still
/// be correct.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and carries no hint.
/// </para>
/// </remarks>
// a default Module only ever has a RowId of 0, i.e. no hint, which is IsValid checks
#pragma warning disable IJDEV0102
internal readonly record struct IlLocalSignatureOrigin(IlModuleIdentity Module, int RowId) {
#pragma warning restore IJDEV0102
	public bool IsValid => RowId > 0;
	public StandaloneSignatureHandle Handle => IsValid
		? MetadataTokens.StandaloneSignatureHandle(RowId)
		: throw new InternalStateException("local signature origin carries no handle");

	// prevent the generated ToString from throwing InternalStateException
	public override string ToString() => IsValid ? $"Module = {Module}, RowId = {RowId}" : "<no hint>";
}
