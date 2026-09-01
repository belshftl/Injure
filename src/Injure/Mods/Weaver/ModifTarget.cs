// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Injure.Mods.Weaver;

[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct ModifTarget : IEquatable<ModifTarget> {
	public string TargetId { get; }
	public MethodBase Method { get; }

	public ModifTarget(string targetId, MethodBase method, Type nextDelegateType) {
		ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(nextDelegateType);
		if (!typeof(Delegate).IsAssignableFrom(nextDelegateType))
			throw new ArgumentException($"type '{nextDelegateType}' is not a delegate type", nameof(nextDelegateType));
		TargetId = targetId;
		Method = method;
	}

	public bool Equals(ModifTarget other) => TargetId == other.TargetId && Method == other.Method;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is ModifTarget other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(TargetId, Method);
	public static bool operator ==(ModifTarget left, ModifTarget right) => left.Equals(right);
	public static bool operator !=(ModifTarget left, ModifTarget right) => !left.Equals(right);
}
