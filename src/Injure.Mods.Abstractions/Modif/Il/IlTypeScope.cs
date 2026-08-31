// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Identifies the metadata scope in which a named type is resolved.
/// </summary>
public abstract class IlTypeScope {
	private protected IlTypeScope() {
	}

	public bool Equals(IlTypeScope? other) => IlRefEquality.ScopeEquals(this, other);
	public sealed override bool Equals([NotNullWhen(true)] object? obj) => obj is IlTypeScope other && Equals(other);
	public sealed override int GetHashCode() => IlRefEquality.ScopeHashCode(this);
	public static bool operator ==(IlTypeScope? left, IlTypeScope? right) => left is null ? right is null : left.Equals(right);
	public static bool operator !=(IlTypeScope? left, IlTypeScope? right) => !(left == right);

	/// <summary>
	/// Identifies a type defined in a specific module.
	/// </summary>
	public sealed class Module : IlTypeScope {
		/// <summary>
		/// The module identity.
		/// </summary>
		public IlModuleIdentity Identity { get; }

		internal Module(IlModuleIdentity identity) => Identity = identity;

		public override string ToString() => Identity.Name;
	}

	/// <summary>
	/// Identifies a type resolved through an assembly reference.
	/// </summary>
	public sealed class Assembly : IlTypeScope {
		/// <summary>
		/// The assembly identity.
		/// </summary>
		public IlAssemblyIdentity Identity { get; }

		internal Assembly(IlAssemblyIdentity identity) => Identity = identity;

		public override string ToString() => Identity.Name;
	}

	/// <summary>
	/// Identifies a type resolved through a module reference.
	/// </summary>
	public sealed class ModuleReference : IlTypeScope {
		/// <summary>
		/// The module reference name.
		/// </summary>
		public string Name { get; }

		internal ModuleReference(string name) => Name = name;

		public override string ToString() => Name;
	}
}
