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

[assembly: ModAssembly("jdoe.test-mod", ModAssemblyHotReloadLevel.SafeBoundary)]

namespace TestMod;

[ModLifetimeIdentityMarker]
public readonly struct TestModL : IModLifetimeIdentity {
}

[ModEntrypoint]
public sealed class Entrypoint : IModEntrypoint<ITestGameModApi, TestModL> {
	public ValueTask LoadAsync(IModLoadContext<ITestGameModApi, TestModL> ctx, GenerationCancellationToken<TestModL> ct) {
		ctx.Diagnostics.Info("loaded!");
		ctx.Api.MarkLoaded(ctx.OwnerID);
		return ValueTask.CompletedTask;
	}

	public ValueTask LinkAsync(IModLinkContext<ITestGameModApi, TestModL> ctx, GenerationCancellationToken<TestModL> ct) =>
		ValueTask.CompletedTask;

	public ValueTask ActivateAsync(GenerationCancellationToken<TestModL> ct) =>
		ValueTask.CompletedTask;

	public ValueTask DeactivateAsync(GenerationCancellationToken<TestModL> ct) =>
		ValueTask.CompletedTask;

	public ValueTask UnloadAsync(GenerationCancellationToken<TestModL> ct) =>
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
