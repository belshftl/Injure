// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.CodeAnalysis.Internal;
using Injure.Mods.Abstractions.Modif;
using Injure.Runtime;

namespace Injure.Mods.Abstractions;

internal static class ModContextCodeAnalysisMessages {
	public const string DontCache =
		"lifecycle context objects are only valid for the duration of the corresponding lifecycle method invocation; cache specific long-lived values such as Api / Scope / Diagnostics";
	public const string DontCapture =
		"lifecycle context objects are only valid for the duration of the corresponding lifecycle method invocation, so a lambda capture probably won't work how you think it will; cache and capture specific long-lived values such as Api / Scope / Diagnostics instead";
}

[DontCache(ModContextCodeAnalysisMessages.DontCache)]
[DontCaptureIntoClosure(ModContextCodeAnalysisMessages.DontCapture)]
[DontImplement]
public interface IModCtx<out TGameApi, L> where L : struct, IModLifetimeIdentity {
	string OwnerId { get; }
	Semver Version { get; }
	TGameApi Api { get; }
	IOwnerDiagnostics Diagnostics { get; }
	IBoundedScope<L> Scope { get; }
	ReloadGeneration Generation { get; }
	DiagnosticsSinkRegistry DiagnosticsSinkRegistry { get; }
}

[DontCache(ModContextCodeAnalysisMessages.DontCache)]
[DontCaptureIntoClosure(ModContextCodeAnalysisMessages.DontCapture)]
[DontImplement]
public interface IModLoadCtx<out TGameApi, L> : IModCtx<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IDetourDecl<L> LoadDetours { get; }
	IPatchDecl<L> LoadPatches { get; }
	IModExportDecl<L> Exports { get; }
}

[DontCache(ModContextCodeAnalysisMessages.DontCache)]
[DontCaptureIntoClosure(ModContextCodeAnalysisMessages.DontCapture)]
[DontImplement]
public interface IModLinkCtx<out TGameApi, L> : IModCtx<TGameApi, L> where L : struct, IModLifetimeIdentity {
	// TODO:
	// IModDetourDecl<L> LinkDetours { get; }
	// IModPatchDecl<L> LinkPatches { get; }

	bool TryGetDep(string ownerId, out LoadedDepInfo<L> info);
	bool TryGetCodeDep(string ownerId, out UntypedLoadedCodeDepInfo<L> info);
	bool TryGetCodeDep<LDep>(out LoadedCodeDepInfo<L, LDep> info) where LDep : struct, IModLifetimeIdentity;

	LoadedDepInfo<L> RequireDep(string ownerId);
	UntypedLoadedCodeDepInfo<L> RequireCodeDep(string ownerId);
	LoadedCodeDepInfo<L, LDep> RequireCodeDep<LDep>() where LDep : struct, IModLifetimeIdentity;
}

[DontCache(ModContextCodeAnalysisMessages.DontCache)]
[DontCaptureIntoClosure(ModContextCodeAnalysisMessages.DontCapture)]
[DontImplement]
public interface IModActivateCtx<out TGameApi, L> : IModCtx<TGameApi, L> where L : struct, IModLifetimeIdentity {
	GameServices GameServices { get; }
	IBoundedScope<L> ActivationScope { get; }
}

[DontCache(ModContextCodeAnalysisMessages.DontCache)]
[DontCaptureIntoClosure(ModContextCodeAnalysisMessages.DontCapture)]
[DontImplement]
public interface IModReloadCtx<out TGameApi, L> : IModCtx<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IReadOnlySet<string> ReloadSet { get; }
	GameServices? GameServices { get; }
}

public sealed class ModLifecycleCtxExpiredException : Exception {
	public string Kind { get; }
	public ReloadGeneration Generation { get; }
	internal ModLifecycleCtxExpiredException(string kind, ReloadGeneration generation) : base(
		$"{kind} object for {generation} is no longer valid past the method return; cache the values you'd like to keep around such as Api/Scope/Diagnostics, don't retain or capture the whole object"
	) {
		Kind = kind;
		Generation = generation;
	}
}
