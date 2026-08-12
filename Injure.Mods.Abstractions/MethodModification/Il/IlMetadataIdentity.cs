// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Identifies a metadata module structurally.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct IlModuleIdentity : IEquatable<IlModuleIdentity> {
	/// <summary>
	/// The module version identifier.
	/// </summary>
	public Guid Mvid { get; }

	/// <summary>
	/// The module name.
	/// </summary>
	public string Name { get; }

	internal IlModuleIdentity(Guid mvid, string name) {
		Mvid = mvid;
		Name = name;
	}

	public bool Equals(IlModuleIdentity other) => Mvid == other.Mvid && Name == other.Name;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlModuleIdentity other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Mvid, Name);
	public static bool operator ==(IlModuleIdentity left, IlModuleIdentity right) => left.Equals(right);
	public static bool operator !=(IlModuleIdentity left, IlModuleIdentity right) => !left.Equals(right);
}

/// <summary>
/// Identifies an assembly structurally.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct IlAssemblyIdentity : IEquatable<IlAssemblyIdentity> {
	/// <summary>
	/// The simple assembly name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The assembly version.
	/// </summary>
	public Version? Version { get; }

	/// <summary>
	/// The culture name, or <see langword="null"/>.
	/// </summary>
	public string? CultureName { get; }

	/// <summary>
	/// The public key token.
	/// </summary>
	public ImmutableArray<byte> PublicKeyToken { get; }

	/// <summary>
	/// The assembly flags.
	/// </summary>
	public AssemblyFlags Flags { get; }

	internal IlAssemblyIdentity(
		string name,
		Version? version,
		string? cultureName,
		ImmutableArray<byte> publicKeyToken,
		AssemblyFlags flags
	) {
		Name = name;
		Version = version;
		CultureName = cultureName;
		PublicKeyToken = publicKeyToken.IsDefault ? [] : publicKeyToken;
		Flags = flags;
	}

	public bool Equals(IlAssemblyIdentity other) =>
		StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name) &&
		Version == other.Version &&
		StringComparer.OrdinalIgnoreCase.Equals(CultureName, other.CultureName) &&
		Flags == other.Flags &&
		PublicKeyToken.AsSpan().SequenceEqual(other.PublicKeyToken.AsSpan());
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlAssemblyIdentity other && Equals(other);
	public override int GetHashCode() {
		HashCode hash = new();
		hash.Add(Name, StringComparer.OrdinalIgnoreCase);
		hash.Add(Version);
		hash.Add(CultureName, StringComparer.OrdinalIgnoreCase);
		hash.Add(Flags);
		foreach (byte value in PublicKeyToken.AsSpan())
			hash.Add(value);
		return hash.ToHashCode();
	}
	public static bool operator ==(IlAssemblyIdentity left, IlAssemblyIdentity right) => left.Equals(right);
	public static bool operator !=(IlAssemblyIdentity left, IlAssemblyIdentity right) => !left.Equals(right);
}

internal static class IlAssemblyIdentityFactory {
	public static IlAssemblyIdentity FromAssemblyName(AssemblyName assemblyName) => new(
		assemblyName.Name ?? throw new InternalStateException("assembly name has no simple name"),
		assemblyName.Version,
		normalizeCulture(assemblyName.CultureName),
		assemblyName.GetPublicKeyToken()?.ToImmutableArray() ?? [],
		normalizeFlags((AssemblyFlags)assemblyName.Flags)
	);

	public static IlAssemblyIdentity FromDefinition(MetadataReader reader, AssemblyDefinition definition) => new(
		reader.GetString(definition.Name),
		definition.Version,
		normalizeCulture(definition.Culture.IsNil ? null : reader.GetString(definition.Culture)),
		toPublicKeyToken(reader.GetBlobBytes(definition.PublicKey), isFullPublicKey: true),
		normalizeFlags(definition.Flags)
	);

	public static IlAssemblyIdentity FromReference(MetadataReader reader, AssemblyReference reference) => new(
		reader.GetString(reference.Name),
		reference.Version,
		normalizeCulture(reference.Culture.IsNil ? null : reader.GetString(reference.Culture)),
		toPublicKeyToken(reader.GetBlobBytes(reference.PublicKeyOrToken), (reference.Flags & AssemblyFlags.PublicKey) != 0),
		normalizeFlags(reference.Flags)
	);

	private static AssemblyFlags normalizeFlags(AssemblyFlags flags) => flags & ~AssemblyFlags.PublicKey;
	private static string? normalizeCulture(string? culture) => string.IsNullOrEmpty(culture) ? null : culture;
	private static ImmutableArray<byte> toPublicKeyToken(byte[] keyOrToken, bool isFullPublicKey) {
		if (keyOrToken.Length == 0)
			return [];
		if (!isFullPublicKey)
			return keyOrToken.ToImmutableArray();
		byte[] hash = SHA1.HashData(keyOrToken);
		Span<byte> token = stackalloc byte[8];
		for (int i = 0; i < token.Length; i++)
			token[i] = hash[hash.Length - 1 - i];
		return token.ToImmutableArray();
	}
}
