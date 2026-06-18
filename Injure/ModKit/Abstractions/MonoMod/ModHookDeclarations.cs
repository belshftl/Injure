// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Reflection;

namespace Injure.ModKit.Abstractions.MonoMod;

public readonly struct ModHookConfig {
	public required string DetourID { get; init; }

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }

	public IReadOnlyList<string>? DetourBefore { get; init; }
	public IReadOnlyList<string>? DetourAfter { get; init; }
	public int? DetourPriority { get; init; }
}

public interface IModHookDeclarations<L> where L : struct, IModLifetimeIdentity {
	void DeclareHook(string targetID, MethodInfo hookMethod, in ModHookConfig config);
	void DeclareHook(MethodBase targetMethod, MethodInfo hookMethod, in ModHookConfig config);

	void DeclareILHook(string targetID, MethodInfo manipulatorMethod, in ModHookConfig config);
	void DeclareILHook(MethodBase targetMethod, MethodInfo manipulatorMethod, in ModHookConfig config);
}
