// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// The provenance of an IL instruction; that is, an identification of the owner that most recently
/// introduced or modified it.
/// </summary>
/// <remarks>
/// <para>
/// Provenance is for composition, diagnostics, and pattern matching; it is not a security feature.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is the unknown-provenance value.
/// </para>
/// </remarks>
public readonly struct IlProvenance : IEquatable<IlProvenance> {
	/// <summary>
	/// The owner that most recently introduced or modified the instruction, or
	/// <see langword="null"/> if the instruction's provenance is unknown.
	/// </summary>
	public string? OwnerId { get; }

	internal IlProvenance(string? ownerId) {
		InternalStateException.ThrowIfNonnullAndInvalidOwnerId(ownerId);
		OwnerId = ownerId;
	}

	public bool Equals(IlProvenance other) => OwnerId == other.OwnerId;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlProvenance other && Equals(other);
	public override int GetHashCode() => OwnerId is null ? 0 : StringComparer.Ordinal.GetHashCode(OwnerId);
	public static bool operator ==(IlProvenance left, IlProvenance right) => left.Equals(right);
	public static bool operator !=(IlProvenance left, IlProvenance right) => !left.Equals(right);

	/// <summary>
	/// Returns either <see cref="OwnerId"/>, or a human-readable fallback string if the instruction's
	/// provenance is unknown.
	/// </summary>
	public override string ToString() => OwnerId ?? "<unknown provenance>";
}

/// <summary>
/// Process-wide intern table mapping owner and manipulator local IDs to dense indices.
/// </summary>
/// <remarks>
/// <para>
/// The purpose of interning is to optimize provenance comparesions in pattern matching and reduce
/// the size of structs containing <see cref="InternalIlProvenance"/>. Index zero always means "unknown".
/// </para>
/// <para>
/// Owner and local IDs share one table. Index equality therefore implies string equality but not
/// equal roles, which is harmless.
/// </para>
/// <para>
/// The table only ever grows. Growth is bounded by the set of distinct IDs a process ever loads,
/// and reloading a mod reuses its existing indexes.
/// </para>
/// </remarks>
internal static class IlProvenanceInterning {
	private static readonly ConcurrentDictionary<string, int> indices = new(StringComparer.Ordinal);
	private static readonly Lock growLock = new();
	private static string[] strings = new string[32];
	private static int count = 1; // index 0 is reserved

	/// <summary>
	/// Interns an ID, returning its dense index. <see langword="null"/> maps to zero.
	/// </summary>
	public static int Intern(string? value) {
		if (value is null)
			return 0;
		if (indices.TryGetValue(value, out int existing))
			return existing;
		lock (growLock) {
			if (indices.TryGetValue(value, out existing))
				return existing;
			if (count == strings.Length)
				Array.Resize(ref strings, checked(strings.Length * 2));
			int index = count;
			strings[index] = value;
			Volatile.Write(ref count, index + 1);
			indices[value] = index;
			return index;
		}
	}

	/// <summary>
	/// Resolves a dense index back to its ID. Index zero resolves to <see langword="null"/>.
	/// </summary>
	public static string? GetString(int index) {
		if (index == 0)
			return null;
		string[] snapshot = strings;
		if ((uint)index >= (uint)Volatile.Read(ref count) || (uint)index >= (uint)snapshot.Length)
			throw new InternalStateException($"provenance intern index {index} is out of range");
		return snapshot[index];
	}
}

/// <summary>
/// The actual provenance info stored for an IL instruction: an interned owner ID / manipulator local
/// ID pair.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is the unknown-provenance value.
/// </remarks>
internal readonly record struct InternalIlProvenance {
	/// <summary>
	/// The interned owner ID index, or 0 for unknown provenance.
	/// </summary>
	public int OwnerIdIndex { get; }

	/// <summary>
	/// The interned manipulator local ID index, or 0 for unknown provenance.
	/// </summary>
	public int LocalIdIndex { get; }

	public InternalIlProvenance(string? ownerId, string? localId) {
		InternalStateException.ThrowIfNonnullAndInvalidOwnerId(ownerId);
		InternalStateException.ThrowIfNonnullAndInvalidLocalId(localId);
		OwnerIdIndex = IlProvenanceInterning.Intern(ownerId);
		LocalIdIndex = IlProvenanceInterning.Intern(localId);
	}

	public string? GetOwnerId() => IlProvenanceInterning.GetString(OwnerIdIndex);
	public string? GetLocalId() => IlProvenanceInterning.GetString(LocalIdIndex);
	public bool IsUnknown => OwnerIdIndex == 0;

	public IlProvenance ToPublic() => new(GetOwnerId());

	public override string ToString() =>
		OwnerIdIndex == 0 ? "<unknown provenance>" :
		LocalIdIndex == 0 ? GetOwnerId()! :
		$"{GetOwnerId()}::{GetLocalId()}";
}
