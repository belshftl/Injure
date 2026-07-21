// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Injure.Weaver.Model;

public readonly struct TargetCandidate : IEquatable<TargetCandidate> {
	public required string ID { get; init; }
	public required MethodDefinition Method { get; init; }
	public required string ContainerName { get; init; }
	public required string ConstantName { get; init; }
	public required string NextDelegateName { get; init; }

	public bool Equals(TargetCandidate other) => ID == other.ID && Method == other.Method &&
		ContainerName == other.ContainerName && ConstantName == other.ConstantName && NextDelegateName == other.NextDelegateName;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is TargetCandidate other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(ID, Method, ContainerName, ConstantName, NextDelegateName);
	public static bool operator ==(TargetCandidate left, TargetCandidate right) => left.Equals(right);
	public static bool operator !=(TargetCandidate left, TargetCandidate right) => !left.Equals(right);
}
