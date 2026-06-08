// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

using Mono.Cecil;

namespace Injure.Weaver.Model;

public enum HookKind {
	Intended,
	Raw,
}

public readonly struct HookCandidate : IEquatable<HookCandidate> {
	public required HookKind Kind { get; init; }
	public required string ID { get; init; }
	public required MethodDefinition Method { get; init; }
	public required string ContainerName { get; init; }
	public required string ConstantName { get; init; }
	public required string OrigDelegateName { get; init; }

	public bool Equals(HookCandidate other) => Kind == other.Kind && ID == other.ID && Method == other.Method &&
		ContainerName == other.ContainerName && ConstantName == other.ConstantName && OrigDelegateName == other.OrigDelegateName;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is HookCandidate other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Kind, ID, Method, ContainerName, ConstantName, OrigDelegateName);
	public static bool operator ==(HookCandidate left, HookCandidate right) => left.Equals(right);
	public static bool operator !=(HookCandidate left, HookCandidate right) => !left.Equals(right);
}
