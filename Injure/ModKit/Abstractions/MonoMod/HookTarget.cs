// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Injure.ModKit.Abstractions.MonoMod;

public readonly struct HookTarget(string id, MethodBase method, Type origDelegateType) : IEquatable<HookTarget> {
	public string ID { get; } = id;
	public MethodBase Method { get; } = method;
	public Type OrigDelegateType { get; } = origDelegateType;

	public bool Equals(HookTarget other) => ID == other.ID && Method == other.Method && OrigDelegateType == other.OrigDelegateType;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is HookTarget other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(ID, Method, OrigDelegateType);
	public static bool operator ==(HookTarget left, HookTarget right) => left.Equals(right);
	public static bool operator !=(HookTarget left, HookTarget right) => !left.Equals(right);
}
