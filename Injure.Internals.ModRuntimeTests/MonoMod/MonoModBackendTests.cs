// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Hooks;
using Injure.Mods.Abstractions.Hooks.Il;
using Injure.Mods.Runtime.MonoMod;

namespace Injure.Internals.ModRuntimeTests.MonoMod;

[CollectionDefinition(nameof(MonoModBackendCollection), DisableParallelization = true)]
public sealed class MonoModBackendCollection;

[Collection(nameof(MonoModBackendCollection))]
public sealed class MonoModBackendTests {
	[ModLifetimeIdentityBelongsTo("MonoModBackendTests")]
	private readonly struct TestL : IModLifetimeIdentity;

	private delegate int next_ManagedTarget(int value);
	private static int managedTargetHook(next_ManagedTarget next, int value) => next(value) + 100;

	private static class Targets {
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int ManagedTarget(int value) => value + 1;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int IlTarget() => NormalIlTargetReturn;
		public const int NormalIlTargetReturn = 1;
		public const int PatchedIlTargetReturn = 42;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int OrderTarget() => NormalOrderTargetReturn;
		public const int NormalOrderTargetReturn = 2;
		public const int PatchedOrderTargetReturn = 8008135;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int DelegateTarget() => NormalDelegateTargetReturn;
		public const int NormalDelegateTargetReturn = 67;
		public const int PatchedDelegateTargetReturnDiv3 = 0xbeef;
		public const int PatchedDelegateTargetReturn = PatchedDelegateTargetReturnDiv3 * 3;
	}

	[Fact]
	public void ManagedHookCanWrapTargetAndDisposeRestoresOriginal() {
		MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.ManagedTarget))!;
		MethodInfo hook = typeof(MonoModBackendTests).GetMethod(nameof(managedTargetHook), BindingFlags.Static | BindingFlags.NonPublic)!;
		MonoModRuntimeHookBackend backend = new();

		Assert.Equal(11, Targets.ManagedTarget(10));
		using (IInstalledRuntimeHook handle = backend.InstallManagedHook(new ManagedHookInstallRequest {
			TargetMethod = target,
			HookMethod = hook,
			OwnerId = "mod",
			LocalId = "hook",
		})) {
			Assert.Equal(111, Targets.ManagedTarget(10));
		}
		Assert.Equal(11, Targets.ManagedTarget(10));
	}

	[Fact]
	public void IlHookPipelineCanReplaceReturnValueAndDisposeRestoresOriginal() {
		MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.IlTarget))!;
		var registration = IlManipulatorRegistration.Create<TestL>(
			"mod",
			"hook",
			static ctx => {
				ctx.EmitAtStart(static e => {
					e.LdcI4(Targets.PatchedIlTargetReturn);
					e.Ret();
				});
			}
		);
		MonoModRuntimeHookBackend backend = new();

		Assert.Equal(Targets.NormalIlTargetReturn, Targets.IlTarget());
		using (IInstalledRuntimeHook handle = backend.InstallIlHookPipeline(new IlHookPipelineInstallRequest {
			TargetMethod = target,
			BaselineOwnerId = "game",
			GetSnapshot = () => [registration],
		})) {
			Assert.Equal(Targets.PatchedIlTargetReturn, Targets.IlTarget());
		}
		Assert.Equal(Targets.NormalIlTargetReturn, Targets.IlTarget());
	}

	[Fact]
	public void IlHookPipelineRunsManipulatorsInSnapshotOrder() {
		MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.OrderTarget))!;
		var first = IlManipulatorRegistration.Create<TestL>(
			"first-mod",
			"hook",
			static ctx => {
				ctx.EmitAtStart(static e => {
					e.LdcI4(1);
					e.Pop();
				});
			}
		);
		var second = IlManipulatorRegistration.Create<TestL>(
			"second-mod",
			"hook",
			static ctx => {
				IlMatch m = ctx.MatchNext(
					[MatchIl.LdcI4(1), MatchIl.Pop],
					IlPatternProvenanceConstraint.AllFromOwner("first-mod")
				);
				m.EmitAfter(static e => {
					e.LdcI4(Targets.PatchedOrderTargetReturn);
					e.Ret();
				});
			}
		);
		MonoModRuntimeHookBackend backend = new();

		Assert.Equal(Targets.NormalOrderTargetReturn, Targets.OrderTarget());
		using (IInstalledRuntimeHook handle = backend.InstallIlHookPipeline(new IlHookPipelineInstallRequest {
			TargetMethod = target,
			BaselineOwnerId = "game",
			GetSnapshot = () => [first, second],
		})) {
			Assert.Equal(Targets.PatchedOrderTargetReturn, Targets.OrderTarget());
		}
		Assert.Equal(Targets.NormalOrderTargetReturn, Targets.OrderTarget());
	}

	[Fact]
	public void ManagedDelegateEmissionUsesMonoModLowerer() {
		MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.DelegateTarget))!;
		var registration = IlManipulatorRegistration.Create<TestL>(
			"mod",
			"hook",
			static ctx => {
				ctx.EmitAtStart(static e => {
					e.LdcI4(Targets.PatchedDelegateTargetReturnDiv3);
					e.Delegate<Func<int, int>>(static x => x * 3);
					e.Ret();
				});
			}
		);
		MonoModRuntimeHookBackend backend = new();

		Assert.Equal(Targets.NormalDelegateTargetReturn, Targets.DelegateTarget());
		using (IInstalledRuntimeHook handle = backend.InstallIlHookPipeline(new IlHookPipelineInstallRequest {
			TargetMethod = target,
			BaselineOwnerId = "game",
			GetSnapshot = () => [registration],
		})) {
			Assert.Equal(Targets.PatchedDelegateTargetReturn, Targets.DelegateTarget());
		}
		Assert.Equal(Targets.NormalDelegateTargetReturn, Targets.DelegateTarget());
	}
}
