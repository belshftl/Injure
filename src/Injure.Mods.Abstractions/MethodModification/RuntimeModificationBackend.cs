// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Abstractions.MethodModification;

internal interface IInstalledRuntimeDetour : IDisposable, IStrongRefDroppable;
internal interface IInstalledRuntimePatch : IDisposable, IStrongRefDroppable;

internal interface IRuntimeDetourBackend : IStrongRefDroppable {
	IInstalledRuntimeDetour Install(in DetourInstallRequest request);
}

internal interface IRuntimePatchBackend : IStrongRefDroppable {
	IInstalledRuntimePatch InstallPipeline(in PatchPipelineInstallRequest request);
}

internal readonly struct DetourInstallRequest {
	public required MethodBase TargetMethod { get; init; }
	public required MethodInfo DetourMethod { get; init; }
	public required string OwnerId { get; init; }
	public required string LocalId { get; init; }
}

internal readonly struct PatchPipelineInstallRequest {
	public required MethodBase TargetMethod { get; init; }
	public required string? BaselineOwnerId { get; init; }
	public required Func<IReadOnlyList<IlManipulatorRegistration>> GetSnapshot { get; init; }
}
