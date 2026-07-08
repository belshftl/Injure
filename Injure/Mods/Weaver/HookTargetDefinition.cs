// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Injure.Mods.Weaver;

[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct HookTargetDefinition : IEquatable<HookTargetDefinition> {
	public string TargetId { get; }
	public MethodBase Method { get; }

	/// <summary>
	/// The delegate type for the next hook in the chain.
	/// </summary>
	public Type NextDelegateType { get; }

	public HookTargetDefinition(string targetId, MethodBase method, Type nextDelegateType) {
		ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(nextDelegateType);
		if (!typeof(Delegate).IsAssignableFrom(nextDelegateType))
			throw new ArgumentException($"type '{nextDelegateType}' is not a delegate type", nameof(nextDelegateType));
		TargetId = targetId;
		Method = method;
		NextDelegateType = nextDelegateType;
	}

	public bool Equals(HookTargetDefinition other) => TargetId == other.TargetId && Method == other.Method && NextDelegateType == other.NextDelegateType;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is HookTargetDefinition other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(TargetId, Method, NextDelegateType);
	public static bool operator ==(HookTargetDefinition left, HookTargetDefinition right) => left.Equals(right);
	public static bool operator !=(HookTargetDefinition left, HookTargetDefinition right) => !left.Equals(right);
}
