// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Diagnostics;
using Injure.Mods;
using Injure.Sched.Coro;

namespace Injure.Tests.Diagnostics;

public sealed class DiagnosticsTests {
	private sealed class RecordingSink : IDiagnosticsSink {
		public List<DiagnosticEvent> Events { get; } = new();
		public void Report(in DiagnosticEvent d) {
			lock (Events)
				Events.Add(d);
		}
	}

	[Fact]
	public static void DefaultSinkFiltersBySeverityAndFormatsPlainly() {
		StringWriter w = new();
		DefaultDiagnosticsSink sink = new(w, colorOutput: false, minimumSeverity: DiagnosticSeverity.Warning);
		IOwnerDiagnostics d = sink.ForOwner("mygame");
		d.Debug("hidden");
		d.Info("hidden too");
		d.Warning("careful");
		d.Error("broken");
		string[] lines = w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Assert.Equal(2, lines.Length);
		Assert.Contains("[mygame@XXXX] [WARN] careful", lines[0]);
		Assert.Contains("[mygame@XXXX] [ERROR] broken", lines[1]);
	}

	[Fact]
	public static void OwnerDiagnosticsAttributeTheirEvents() {
		RecordingSink sink = new();
		sink.ForOwner("mymod", new ReloadGeneration("mymod", 3)).Info("hello");
		DiagnosticEvent e = Assert.Single(sink.Events);
		Assert.Equal("mymod", e.SourceOwnerId);
		Assert.Equal(DiagnosticSeverity.Info, e.Severity);
		Assert.Equal("hello", e.Message);
		Assert.Equal(3ul, e.Generation!.Value.Value);

		Assert.Throws<ArgumentException>(() => sink.ForOwner("not valid!"));
		Assert.Throws<ArgumentException>(() => sink.ForOwner("mymod", new ReloadGeneration("othermod", 1)));
	}

	[Fact]
	public static void RegistriesForwardToEverySinkAddedSoFar() {
		RecordingSink first = new(), added = new();
		DiagnosticsSinkRegistry registry = new([first]);
		IOwnerDiagnostics d = registry.ForOwner("mygame");
		d.Info("one");
		DiagnosticsSinkRegistration reg = registry.Add(added);
		d.Info("two");
		reg.Remove();
		d.Info("three");
		Assert.Equal(["one", "two", "three"], first.Events.Select(static e => e.Message));
		Assert.Equal(["two"], added.Events.Select(static e => e.Message));
	}

	private static IEnumerator<CoroYield> fault() {
		throw new InvalidOperationException("boom");
#pragma warning disable CS0162 // unreachable code detected
		yield break;
#pragma warning restore CS0162
	}

	[Fact]
	public static void CoroutineFaultsAreReportedToTheirOwner() {
		RecordingSink sink = new();
		CoroScheduler scheduler = new() { UnhandledFaultMode = CoroUnhandledFaultMode.LogAfterTick, DiagnosticsSink = sink };
		var scope = CoroScope.CreateRoot(scheduler, "test", "mymod");
		scheduler.Start(fault(), scope);
		scheduler.Tick(0.016, 0.016, CoroUpdatePhase.Update);
		scheduler.Tick(0.016, 0.016, CoroUpdatePhase.Update);
		DiagnosticEvent e = Assert.Single(sink.Events);
		Assert.Equal("mymod", e.SourceOwnerId);
		Assert.Equal(DiagnosticSeverity.Error, e.Severity);
		Assert.Contains("boom", e.Message);
	}
}
