// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using System.Threading.Tasks;
using Injure;
using Injure.ModKit.Abstractions;
using Injure.ModKit.Abstractions.MonoMod;
using Injure.ModKit.Mods.MonoMod;
using MonoMod.Cil;
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
		/*
		ctx.LoadHooks.DeclareILHook(TestGame.RawHooks.GameplayLayer.GetSomeColor, IL_GameplayLayer_GetSomeColor, new ModHookConfig {
			DetourID = "jdoe.test-mod::SomeHook",
		});
		*/
		ctx.Exports.Add<ITestModExports>(new ExportsImpl(ctx.Diagnostics));
		ctx.Diagnostics.Info("loaded!");
		ctx.Api.MarkLoaded(ctx.OwnerID);
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

	[LoadILHook(TestGame.RawHooks.GameplayLayer.GetSomeColor)]
	public static void IL_GameplayLayer_GetSomeColor(ILContext il) {
		ILCursor c = new(il);
		c.RequireGotoNext("ldsfld Injure.Color32::Magenta", static i => i.MatchLdsfld<Color32>(nameof(Color32.Magenta)));
		FieldInfo fi = typeof(Color32).GetField(nameof(Color32.Green), BindingFlags.Static | BindingFlags.Public) ??
			throw new MissingFieldException("Color32.Green unexpectedly missing");
		c.Remove(); // TODO: avoid destructive IL edits, they can mess up IL hooks from other mods
		c.EmitLdsfld(fi);
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
