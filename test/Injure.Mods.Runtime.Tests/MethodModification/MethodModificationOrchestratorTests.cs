// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;
using Injure.Mods.Runtime.MethodModification;
using Injure.Mods.Runtime.MethodModification.Detours;
using Injure.Mods.Runtime.MethodModification.Profiler;
using Injure.Mods.Runtime.Tests.MethodModification.Profiler;

namespace Injure.Mods.Runtime.Tests.MethodModification;

internal sealed class RecordingDetourTransform : IDetourTransform {
	public List<(MethodIdentity Method, int DetourCount)> Calls { get; } = new();

	public IlMethodBody Apply(MethodIdentity method, IlMethodBody body, ImmutableArray<DetourRegistration> detours) {
		Calls.Add((method, detours.Length));
		IlTransactionCore core = new(body, IlTest.OwnerId, null, default, null);
		core.EmitAtBoundary(0, static e => e.Nop());
		core.Commit();
		return body;
	}
}

public sealed class MethodModificationOrchestratorTests : IDisposable {
	private const string targetType = nameof(IlFixture.Mechanism);
	private const string targetMethod = nameof(IlFixture.Mechanism.Sizeof);
	private const string otherMethod = nameof(IlFixture.Mechanism.Peek);

	private readonly FakeProfilerHost host = new();
	private readonly MethodModificationRegistry registry = new(new LoadOrder("first", "second"));
	private readonly MethodTransformCache cache = new();
	private readonly RecordingDetourTransform detours = new();
	private readonly MethodModificationOrchestrator orchestrator;
	private readonly ModuleInfo module;
	private readonly MethodIdentity target;
	private readonly MethodIdentity other;

	public MethodModificationOrchestratorTests() {
		string location = typeof(IlFixture.Mechanism).Assembly.Location;
		Assert.SkipWhen(string.IsNullOrEmpty(location), "fixture assembly has no on-disk location");
		module = host.LoadModule(location);
		target = host.FindMethod(module.Id, targetType, targetMethod);
		other = host.FindMethod(module.Id, targetType, otherMethod);
		orchestrator = new MethodModificationOrchestrator(host, registry, cache, default, null, detours);
	}

	public void Dispose() {
		orchestrator.Dispose();
		host.Dispose();
	}

	// ==========================================================================================
	// helpers
	private static IlManipulatorRegistration nop(string ownerId, string localId) =>
		IlManipulatorRegistration.Create<TestL>(ownerId, localId, static ctx => ctx.EmitAtStart(static e => e.Nop()));

	private static IlManipulatorRegistration throws(string ownerId, string localId) =>
		IlManipulatorRegistration.Create<TestL>(
			ownerId, localId, static _ => throw new InvalidTimeZoneException("from the manipulator")
		);

	private static IlManipulatorRegistration invalid(string ownerId, string localId) =>
		IlManipulatorRegistration.Create<TestL>(ownerId, localId, static ctx => ctx.EmitAtStart(static e => e.Pop()));

	private static IlManipulatorRegistration callsExternal(string ownerId, string localId) =>
		IlManipulatorRegistration.Create<TestL>(ownerId, localId, static ctx =>
			ctx.EmitAtStart(static e => e.Call(IlReferenceFactory.Method(
				typeof(MethodModificationOrchestratorTests).GetMethod(
					nameof(stub), BindingFlags.Static | BindingFlags.NonPublic
				)!
			)))
		);

	private static DetourRegistration detour(string ownerId, string localId) =>
		new(ownerId, localId, typeof(MethodModificationOrchestratorTests).GetMethod(
			nameof(stub), BindingFlags.Static | BindingFlags.NonPublic
		)!);

	private static void stub() {
	}

	private int instructionCountOf(MethodIdentity method) {
		byte[] body = host.GetPreparedBody(method) ?? throw new InvalidOperationException("no prepared body");
		var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MethodDefToken);
		unsafe {
			fixed (byte* p = body)
				return SrmMethodBodyDecoder
					.Decode(host.GetMetadata(method.Module), handle, new BlobReader(p, body.Length), default)
					.Instructions.Count;
		}
	}

	private int baselineInstructionCount(MethodIdentity method) {
		ImmutableArray<byte> il = host.GetBaselineIl(method);
		var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MethodDefToken);
		unsafe {
			fixed (byte* p = il.AsSpan())
				return SrmMethodBodyDecoder
					.Decode(host.GetMetadata(method.Module), handle, new BlobReader(p, il.Length), default)
					.Instructions.Count;
		}
	}

	// ==========================================================================================
	// applying
	[Fact]
	public void AnEmptyPassDoesNothing() {
		ApplyResult result = orchestrator.ApplyPending();

		Assert.Empty(result.Applied);
		Assert.Empty(result.Reverted);
		Assert.Empty(host.ReJitRequests);
	}

	[Fact]
	public void APatchIsPreparedAndRequested() {
		int before = baselineInstructionCount(target);
		registry.AddManipulator(target, nop("first", "a"));

		ApplyResult result = orchestrator.ApplyPending();

		Assert.Equal([target], result.Applied);
		Assert.Equal([target], Assert.Single(host.ReJitRequests));
		Assert.Equal(before + 1, instructionCountOf(target));
	}

	[Fact]
	public void ManipulatorsAccumulateAcrossPasses() {
		int before = baselineInstructionCount(target);
		registry.AddManipulator(target, nop("first", "a"));
		orchestrator.ApplyPending();
		registry.AddManipulator(target, nop("second", "b"));
		orchestrator.ApplyPending();

		Assert.Equal(before + 2, instructionCountOf(target));
	}

	[Fact]
	public void OneBatchProducesOneRequestForEveryMethod() {
		registry.AddManipulator(target, nop("first", "a"));
		registry.AddManipulator(other, nop("first", "a"));

		ApplyResult result = orchestrator.ApplyPending();

		Assert.Equal(2, result.Applied.Length);
		Assert.Equal(2, Assert.Single(host.ReJitRequests).Length);
	}

	[Fact]
	public void OneBatchCommitsMetadataOnce() {
		registry.AddManipulator(target, callsExternal("first", "a"));
		registry.AddManipulator(other, callsExternal("first", "a"));

		orchestrator.ApplyPending();

		FakeMetadataEmitter emitter = host.GetEmitter(module.Id);
		Assert.NotEmpty(emitter.Defined);
		Assert.Equal(1, emitter.Commits);
	}

	[Fact]
	public void AnUnchangedMethodIsntReappliedBecauseItsUpToDate() {
		registry.AddManipulator(target, nop("first", "a"));
		orchestrator.ApplyPending();

		ApplyResult result = orchestrator.Apply([target]);

		Assert.Empty(result.Applied);
		Assert.Equal(1, result.UpToDate);
		Assert.Single(host.ReJitRequests);
	}

	// ==========================================================================================
	// detours
	[Fact]
	public void FirstDetourAppliesTheTransform() {
		int before = baselineInstructionCount(target);
		registry.AddDetour(target, detour("first", "d"));

		orchestrator.ApplyPending();

		Assert.Equal((target, 1), Assert.Single(detours.Calls));
		Assert.Equal(before + 1, instructionCountOf(target));
	}

	[Fact]
	public void FirstDetourOnAPatchedMethodReusesTheTransformedBody() {
		registry.AddManipulator(target, nop("first", "a"));
		orchestrator.ApplyPending();
		int patchGeneration = registry.GetGeneration(target).Patch;

		registry.AddDetour(target, detour("first", "d"));
		orchestrator.ApplyPending();

		// the pipeline shouldn't've run again and the transformed body should still be cached at
		// the same generation
		Assert.Equal(patchGeneration, registry.GetGeneration(target).Patch);
		Assert.True(cache.TryGetTransformed(target, patchGeneration, out _));
		Assert.Single(detours.Calls);
	}

	[Fact]
	public void AnAdditionalDetourRequiresNoWork() {
		registry.AddDetour(target, detour("first", "d1"));
		orchestrator.ApplyPending();

		registry.AddDetour(target, detour("first", "d2"));
		ApplyResult result = orchestrator.ApplyPending();

		Assert.Empty(result.Applied);
		Assert.Single(detours.Calls);
		Assert.Single(host.ReJitRequests);
	}

	[Fact]
	public void RemovingTheLastDetourDropsThePrologue() {
		int before = baselineInstructionCount(target);
		registry.AddManipulator(target, nop("first", "a"));
		registry.AddDetour(target, detour("first", "d"));
		orchestrator.ApplyPending();
		Assert.Equal(before + 2, instructionCountOf(target));

		registry.Remove(target, "first", "d");
		orchestrator.ApplyPending();

		Assert.Equal(before + 1, instructionCountOf(target));
	}

	[Fact]
	public void DetoursAreInChainOrder() {
		registry.AddDetour(target, detour("second", "b"));
		registry.AddDetour(target, detour("first", "a"));
		orchestrator.ApplyPending();
		detours.Calls.Clear();

		registry.AddManipulator(target, nop("first", "m"));
		orchestrator.ApplyPending();

		Assert.Equal((target, 2), Assert.Single(detours.Calls));
	}

	// ==========================================================================================
	// removal and revert
	[Fact]
	public void RemovingOneOwnerRetransformsRatherThanReverts() {
		int before = baselineInstructionCount(target);
		registry.AddManipulator(target, nop("first", "a"));
		registry.AddManipulator(target, nop("second", "b"));
		orchestrator.ApplyPending();

		registry.RemoveOwner("first");
		ApplyResult result = orchestrator.ApplyPending();

		Assert.Equal([target], result.Applied);
		Assert.Empty(result.Reverted);
		Assert.Empty(host.RevertRequests);
		Assert.Equal(before + 1, instructionCountOf(target));
	}

	[Fact]
	public void RemovingTheLastOwnerReverts() {
		registry.AddManipulator(target, nop("first", "a"));
		orchestrator.ApplyPending();

		registry.RemoveOwner("first");
		ApplyResult result = orchestrator.ApplyPending();

		Assert.Equal([target], result.Reverted);
		Assert.Equal([target], Assert.Single(host.RevertRequests));
		Assert.Null(host.GetPreparedBody(target));
	}

	[Fact]
	public void MethodThatWasNeverInstalledIsNotReverted() {
		ApplyResult result = orchestrator.Apply([target]);

		Assert.Empty(result.Reverted);
		Assert.Empty(host.RevertRequests);
	}

	[Fact]
	public void RevertingKeepsTheBaselineButDropsTheRest() {
		registry.AddManipulator(target, nop("first", "a"));
		orchestrator.ApplyPending();
		int generation = registry.GetGeneration(target).Patch;

		registry.RemoveOwner("first");
		orchestrator.ApplyPending();

		Assert.True(cache.TryGetBaseline(target, out _));
		Assert.False(cache.TryGetTransformed(target, generation, out _));
	}

	[Fact]
	public void RepatchingAfterRevertDoesntHitStaleEntry() {
		int before = baselineInstructionCount(target);
		registry.AddManipulator(target, nop("first", "a"));
		registry.AddManipulator(target, nop("first", "b"));
		orchestrator.ApplyPending();
		registry.RemoveOwner("first");
		orchestrator.ApplyPending();

		registry.AddManipulator(target, nop("second", "c"));
		orchestrator.ApplyPending();

		Assert.Equal(1, registry.GetGeneration(target).Patch);
		Assert.Equal(before + 1, instructionCountOf(target));
	}

	// ==========================================================================================
	// failure
	[Fact]
	public void AThrowingManipulatorAbandonsThePassAndNamesItsOwner() {
		registry.AddManipulator(target, throws("first", "bad"));

		MethodModificationException ex = Assert.Throws<MethodModificationException>(orchestrator.ApplyPending);

		Assert.Equal(target, ex.Method);
		Assert.Equal("first", ex.OwnerId);
		Assert.Equal("bad", ex.LocalId);
		Assert.IsType<InvalidTimeZoneException>(ex.InnerException?.InnerException);
	}

	[Fact]
	public void AnInvalidBodyAbandonsThePassAndNamesItsOwner() {
		registry.AddManipulator(target, invalid("second", "bad"));

		MethodModificationException ex = Assert.Throws<MethodModificationException>(orchestrator.ApplyPending);

		Assert.Equal("second", ex.OwnerId);
		Assert.Equal("bad", ex.LocalId);
	}

	[Fact]
	public void AFailedPassInstallsNothing() {
		registry.AddManipulator(target, nop("first", "good"));
		registry.AddManipulator(other, throws("first", "bad"));

		Assert.Throws<MethodModificationException>(orchestrator.ApplyPending);

		Assert.Empty(host.ReJitRequests);
	}

	[Fact]
	public void AFailedPassLeavesItsMethodsDirty() {
		registry.AddManipulator(target, throws("first", "bad"));
		Assert.Throws<MethodModificationException>(orchestrator.ApplyPending);

		Assert.Equal([target], registry.DrainDirty());
	}

	[Fact]
	public void ExcludingTheFailingOwnerAndRetryingSucceeds() {
		int before = baselineInstructionCount(target);
		registry.AddManipulator(target, nop("first", "good"));
		registry.AddManipulator(target, throws("second", "bad"));

		MethodModificationException ex = Assert.Throws<MethodModificationException>(orchestrator.ApplyPending);
		registry.RemoveOwner(ex.OwnerId!);
		ApplyResult result = orchestrator.ApplyPending();

		Assert.Equal([target], result.Applied);
		Assert.Equal(before + 1, instructionCountOf(target));
	}

	[Fact]
	public void AFailedPassStillCompletesItsReverts() {
		registry.AddManipulator(target, nop("first", "a"));
		orchestrator.ApplyPending();
		registry.RemoveOwner("first");
		registry.AddManipulator(other, throws("first", "bad"));

		Assert.Throws<MethodModificationException>(orchestrator.ApplyPending);

		Assert.Equal([target], Assert.Single(host.RevertRequests));
		// the reverted method has no entry, so it is not dirtied back into the next pass
		Assert.Equal([other], registry.DrainDirty());
	}

	// ==========================================================================================
	// module unload
	[Fact]
	public void UnloadingAModuleForgetsIt() {
		registry.AddManipulator(target, nop("first", "a"));
		orchestrator.ApplyPending();

		orchestrator.OnModuleUnloading(module.Id);

		Assert.Empty(registry.ModifiedMethods);
		Assert.Equal(0, cache.Count);
		Assert.Empty(registry.DrainDirty());
	}

	[Fact]
	public void UnloadingDoesntRequestAnything() {
		registry.AddManipulator(target, nop("first", "a"));
		orchestrator.ApplyPending();
		host.ReJitRequests.Clear();

		orchestrator.OnModuleUnloading(module.Id);
		orchestrator.ApplyPending();

		Assert.Empty(host.ReJitRequests);
		Assert.Empty(host.RevertRequests);
	}
}
