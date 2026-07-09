// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Hooks;
using Injure.Mods.Abstractions.Hooks.Il;
using Injure.Primitives;
using TestGame.ModApi;
using TestMod.Contracts;

[assembly: ModAssembly("jdoe.test-mod", ModAssemblyHotReloadLevel.Live, typeof(TestModL))]

namespace TestMod;

internal sealed class ExportsImpl(IOwnerDiagnostics log) : ITestModExports {
	private readonly IOwnerDiagnostics log = log;

	public void DoSomething() {
		log.Info("DoSomething() called!");
	}
}

[ModEntrypoint]
public sealed class Entrypoint : IModEntrypoint<ITestGameModApi, TestModL> {
	public ValueTask LoadAsync(IModLoadContext<ITestGameModApi, TestModL> ctx, BoundedCt<TestModL> ct) {
		ctx.Exports.Add<ITestModExports>(new ExportsImpl(ctx.Diagnostics));
		ctx.Diagnostics.Info("loaded!");
		ctx.Api.MarkLoaded(ctx.OwnerId);
		return ValueTask.CompletedTask;
	}

	public ValueTask LinkAsync(IModLinkContext<ITestGameModApi, TestModL> ctx, BoundedCt<TestModL> ct) =>
		ValueTask.CompletedTask;

	public ValueTask ActivateAsync(IModActivateContext<ITestGameModApi, TestModL> ctx, BoundedCt<TestModL> ct) =>
		ValueTask.CompletedTask;

	public ValueTask DeactivateAsync(BoundedCt<TestModL> ct) =>
		ValueTask.CompletedTask;

	public ValueTask UnloadAsync(BoundedCt<TestModL> ct) =>
		ValueTask.CompletedTask;

	[LoadIlHook(TestGame.RawHooks.GameplayLayer.GetSomeColor)]
	internal static void IL_GameplayLayer_GetSomeColor(IlContext<TestModL> ctx) {
		IlMatch m = ctx.MatchNext(
			[MatchIl.Ldsfld<Color32>("Magenta")],
			IlPatternProvenanceConstraint.AllFromOwner("TestGame")
		);
		m.EmitAfter(e => {
			e.Delegate<Func<Color32, Color32>>(static color => color.WithA(0x55));
		});
	}
}

[ModReloadEntrypoint]
public sealed class ReloadEntrypoint : IModReloadEntrypoint<ITestGameModApi, TestModL> {
	public ValueTask<ModLiveStateBlob> SaveStateAsync(IModReloadContext<ITestGameModApi, TestModL> ctx, BoundedCt<TestModL> ct) {
		ctx.Diagnostics.Info("saving live state...");
		return new ValueTask<ModLiveStateBlob>(ModLiveStateBlob.FromUtf8(new(0, 1, 0), ":3"));
	}

	public ValueTask RestoreStateAsync(IModReloadContext<ITestGameModApi, TestModL> ctx, ModLiveStateBlob state, BoundedCt<TestModL> ct) {
		ctx.Diagnostics.Info("restoring live state...");
		return ValueTask.CompletedTask;
	}
}
