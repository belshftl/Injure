// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using Injure.Mods;
using TestGame.ModApi;

using TestMod.Contracts;

[assembly: ModAssembly("jdoe.other-test-mod", ModAssemblyHotReloadLevel.SafeBoundary, typeof(OtherTestMod.OtherTestModL))]

namespace OtherTestMod;

[ModLifetimeIdentityBelongsTo("jdoe.other-test-mod")]
public readonly struct OtherTestModL : IModLifetimeIdentity {
}

[ModEntrypoint]
public sealed class Entrypoint : IModEntrypoint<ITestGameModApi, OtherTestModL> {
	public ValueTask LoadAsync(IModLoadContext<ITestGameModApi, OtherTestModL> ctx, BoundedCt<OtherTestModL> ct) {
		ctx.Diagnostics.Info("loaded!");
		ctx.Api.MarkLoaded(ctx.OwnerID);
		return ValueTask.CompletedTask;
	}

	public ValueTask LinkAsync(IModLinkContext<ITestGameModApi, OtherTestModL> ctx, BoundedCt<OtherTestModL> ct) {
		ITestModExports x = ctx.RequireCodeDependency<TestModL>().Exports.Require<ITestModExports>();
		x.DoSomething();
		return ValueTask.CompletedTask;
	}

	public ValueTask ActivateAsync(IModActivateContext<ITestGameModApi, OtherTestModL> ctx, BoundedCt<OtherTestModL> ct) =>
		ValueTask.CompletedTask;

	public ValueTask DeactivateAsync(BoundedCt<OtherTestModL> ct) =>
		ValueTask.CompletedTask;

	public ValueTask UnloadAsync(BoundedCt<OtherTestModL> ct) =>
		ValueTask.CompletedTask;
}
