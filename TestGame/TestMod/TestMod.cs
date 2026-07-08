// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using System.Threading.Tasks;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Hooks;
using Injure.Mods.Abstractions.Hooks.Il;
using Injure.Primitives;
using Mono.Cecil;
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
		FieldReference magenta = ctx.Imports.Import(
			typeof(Color32).GetField(nameof(Color32.Magenta), BindingFlags.Static | BindingFlags.Public) ??
				throw new MissingFieldException("Color32.Magenta unexpectedly missing")
		);
		FieldReference blue = ctx.Imports.Import(
			typeof(Color32).GetField(nameof(Color32.Blue), BindingFlags.Static | BindingFlags.Public) ??
				throw new MissingFieldException("Color32.Blue unexpectedly missing")
		);

		IlMatch m = ctx.MatchNext(
			[MatchIl.Ldsfld(magenta)],
			IlPatternProvenanceConstraint.AllFromOwner("TestGame")
		);
		IlLabel skip = ctx.DefineLabel();
		m.EmitBefore(e => e.Br(skip));
		m.EmitAfter(e => {
			e.MarkLabel(skip);
			e.Ldsfld(blue);
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
