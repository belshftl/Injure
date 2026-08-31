// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Tests.Modif.E2e;

[Collection(E2eCollection.Name)]
public sealed class ProfilerExtraTests(E2eFixture fixture) {
	private readonly E2eFixture fxt = fixture;

	// ============================================================================================
	// metadata
	private static class MathTarget {
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Compute() => 0;
	}

	private static class ResultHolder {
		public static int Value;
	}

	/// <summary>
	/// Patches in a call to <see cref="Math.Max(int, int)"/> into a method that has never referenced
	/// corelib's <c>System.Math</c>, which should force <c>ProfilerMetadataEmitter</c> to define an
	/// <c>AssemblyRef</c>, <c>TypeRef</c>, and <c>MemberRef</c>, and then have the JIT resolve them.
	/// </summary>
	[Fact]
	public void PatchCanCallARealCorelibMethodItHasNeverReferenced() {
		MethodIdentity method = fxt.GetIdentity(typeof(MathTarget).GetMethod(
			nameof(MathTarget.Compute),
			BindingFlags.Static | BindingFlags.Public
		)!);
		IlMethodRef max = IlRefFactory.Method(typeof(Math).GetMethod(
			nameof(Math.Max),
			BindingFlags.Static | BindingFlags.Public,
			[typeof(int), typeof(int)]
		)!);

		fxt.Registry.AddManipulator(
			method,
			IlManipulatorRegistration.Create<E2eL>("e2e.corelib", "call-max", ctx => {
				IlFieldRef slot = IlRefFactory.Field(typeof(ResultHolder).GetField(
					nameof(ResultHolder.Value),
					BindingFlags.Static | BindingFlags.Public
				)!);
				ctx.EmitAtStart(e => {
					e.LdcI4(3);
					e.LdcI4(7);
					e.Call(max);
					e.Stsfld(slot);
				});
			})
		);

		ApplyResult result = fxt.Orchestrator.ApplyPending();

		Assert.Contains(method, result.Applied);
		ResultHolder.Value = -1;
		MathTarget.Compute();
		Assert.Equal(7, ResultHolder.Value);
	}

	// ============================================================================================
	// ReJIT failure behavior
	private static class FailureTarget {
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Compute() => 777;
	}

	/// <summary>
	/// Installs a deliberately malformed body (tiny header that claims to have 10 bytes of code
	/// and supplies zero) and asserts that the runtime rejects it rather than corrupting the method
	/// or the process, and that it can be reverted back to normal.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The encoder would never emit something like this, so exercising this at all requires bypassing
	/// the usual pipeline. If the JIT's IL verifier is not well-behaved on some target, this may
	/// crash the process; if it does, that's a valuable finding, not a flaw of the test.
	/// </para>
	/// <para>
	/// </remarks>
	[Fact]
	public void MalformedBodyIsCleanlyRejectedAndCanBeReverted() {
		MethodInfo target = typeof(FailureTarget).GetMethod(
			nameof(FailureTarget.Compute),
			BindingFlags.Static | BindingFlags.Public
		)!;
		MethodIdentity method = fxt.GetIdentity(target);
		Assert.Equal(777, FailureTarget.Compute());

		byte[] malformed = [((10 << 2) | 0x02)]; // tiny header, declares 10 code bytes, supplies 0
		fxt.Host.SetPreparedBody(method, malformed);
		fxt.Host.RequestReJit([method]);

		// RequestReJit alone doesn't force an immediate ReJIT; this seems to do the trick
		// the exception is thrown out of the call synchronously rather than later on another thread
		Assert.Throws<InvalidProgramException>(() => RuntimeHelpers.PrepareMethod(target.MethodHandle));

		fxt.Host.RequestRevert([method]);

		Assert.Equal(777, FailureTarget.Compute());
	}

	// ============================================================================================
	// module identity

	/// <summary>
	/// Confirms that a <see cref="ModuleInfo"/> obtained from actual <c>GetModuleInfo2</c> and
	/// <c>GetScopeProps</c> calls is correct; this is the only place in tests where one is read
	/// from real metadata rather than constructed by hand.
	/// </summary>
	[Fact]
	public void ThisAssemblysModulesModuleInfoIsCorrect() {
		Module thisModule = typeof(ProfilerExtraTests).Module;
		MethodIdentity identity = fxt.GetIdentity(
			typeof(ProfilerExtraTests).GetMethod(
				nameof(ThisAssemblysModulesModuleInfoIsCorrect),
				BindingFlags.Instance | BindingFlags.Public
			)!
		);

		ModuleInfo info = fxt.Host.GetLoadedModules().Single(m => m.Id == identity.Module);

		Assert.False(info.IsCollectible);
		Assert.False(string.IsNullOrEmpty(info.Path));
		Assert.Equal(thisModule.ModuleVersionId, info.Mvid);
	}
}
