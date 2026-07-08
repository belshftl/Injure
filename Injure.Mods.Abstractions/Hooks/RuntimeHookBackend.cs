// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions.Hooks.Il;

namespace Injure.Mods.Abstractions.Hooks;

internal interface IInstalledRuntimeHook : IDisposable, IStrongRefDroppable;

internal interface IRuntimeHookBackend : IStrongRefDroppable {
	IInstalledRuntimeHook InstallManagedHook(in ManagedHookInstallRequest request);
	IInstalledRuntimeHook InstallIlHookPipeline(in IlHookPipelineInstallRequest request);
}

internal readonly struct ManagedHookInstallRequest {
	public required MethodBase TargetMethod { get; init; }
	public required MethodInfo HookMethod { get; init; }
	public required string OwnerId { get; init; }
	public required string LocalId { get; init; }
}

internal readonly struct IlHookPipelineInstallRequest {
	public required MethodBase TargetMethod { get; init; }
	public required string? BaselineOwnerId { get; init; }
	public required Func<IReadOnlyList<IlManipulatorRegistration>> GetSnapshot { get; init; }
}
