// SPDX-License-Identifier: MIT

using System.Collections.Frozen;

namespace Injure.Weaver.Model;

public readonly struct AssemblyAnalysis {
	public required FrozenSet<string> OriginallyNonPublicTypeFullNames { get; init; }
	public required FrozenDictionary<string, PublicizedStateMachineKind> StateMachineTypeFullNames { get; init; }
}
