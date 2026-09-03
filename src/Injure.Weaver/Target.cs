// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;

namespace Injure.Weaver;

public enum TargetKind {
	Method,
	StateMachineMoveNext,
	StateMachineDispose,
	LocalFunction,
	Lambda,
}

public sealed class Target {
	public required MethodDefinition Method { get; init; }
	public required TargetKind Kind { get; init; }

	public required string Suffix { get; init; }

	public bool SelfAsObject { get; init; }

	public string Name { get; set; } = "";
}

public sealed class NameGroup {
	public MethodDefinition? Method { get; init; }
	public required TypeDefinition MirrorOwner { get; init; }
	public required string BaseName { get; init; }
	public required string CanonicalKey { get; init; }
	public List<Target> Targets { get; } = new();

	public int Tier { get; set; } = 1;
	public string Resolved { get; set; } = "";
}

public sealed class MirrorScope {
	public required TypeDefinition Source { get; init; }
	public List<NameGroup> Groups { get; } = new();
	public List<MirrorScope> Children { get; } = new();
	public string MirrorName { get; set; } = "";
}
