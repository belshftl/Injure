// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Injure.Internals.Analyzers.Attributes;
using Injure.ModKit.Abstractions.CodeAnalysis;

namespace Injure.ModKit.Abstractions;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DiagnosticSeverity {
	public enum Case {
		Debug = 1,
		Info,
		Warning,
		Error,
	}
}

public readonly record struct DiagnosticEvent(
	string SourceOwnerID,
	DiagnosticSeverity Severity,
	string Message,
	ReloadGeneration? Generation = null
);

public interface IDiagnosticsSink {
	void Report(in DiagnosticEvent d);
}

public sealed class CompositeDiagnosticsSink(params IDiagnosticsSink[] sinks) : IDiagnosticsSink {
	private readonly IDiagnosticsSink[] sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));

	public void Report(in DiagnosticEvent d) {
		foreach (IDiagnosticsSink sink in sinks)
			sink.Report(in d);
	}
}

public sealed class DiagnosticsSinkRegistration : IDisposable, IReloadTeardown {
	private DiagnosticsSinkHub? hub;
	private IDiagnosticsSink? sink;

	internal DiagnosticsSinkRegistration(DiagnosticsSinkHub hub, IDiagnosticsSink sink) {
		this.hub = hub;
		this.sink = sink;
	}

	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public void Dispose() {
		DiagnosticsSinkHub? h = Interlocked.Exchange(ref hub, null);
		IDiagnosticsSink? s = Interlocked.Exchange(ref sink, null);
		if (h is not null && s is not null)
			h.Remove(s);
	}

	public void Teardown(in ReloadTeardownContext ctx) => Dispose();
}

public sealed class DiagnosticsSinkHub(IEnumerable<IDiagnosticsSink> initial) : IDiagnosticsSink {
	private readonly Lock registryLock = new();
	private IDiagnosticsSink[] sinks = initial.ToArray();

	public DiagnosticsSinkRegistration Add(IDiagnosticsSink sink) {
		ArgumentNullException.ThrowIfNull(sink);
		lock (registryLock) {
			IDiagnosticsSink[] old = sinks;
			IDiagnosticsSink[] @new = new IDiagnosticsSink[old.Length + 1];
			Array.Copy(old, @new, old.Length);
			@new[^1] = sink;
			Volatile.Write(ref sinks, @new);
		}
		return new DiagnosticsSinkRegistration(this, sink);
	}

	public void Report(in DiagnosticEvent d) {
		IDiagnosticsSink[] snapshot = Volatile.Read(ref sinks);
		foreach (IDiagnosticsSink sink in snapshot) {
			try {
				sink.Report(in d);
			} catch {
				// TODO maybe report to a fallback sink
			}
		}
	}

	internal void Remove(IDiagnosticsSink sink) {
		lock (registryLock) {
			IDiagnosticsSink[] old = sinks;
			int idx = Array.IndexOf(old, sink);
			if (idx < 0)
				return;

			IDiagnosticsSink[] @new = new IDiagnosticsSink[old.Length - 1];
			Array.Copy(old, 0, @new, 0, idx);
			Array.Copy(old, idx + 1, @new, idx, old.Length - idx - 1);
			Volatile.Write(ref sinks, @new);
		}
	}
}

public interface IOwnerDiagnostics {
	void Log(DiagnosticSeverity severity, string message);
	void Debug(string message);
	void Info(string message);
	void Warning(string message);
	void Error(string message);
}
