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

public static class DiagnosticsSinkExtensions {
	extension(IEnumerable<IDiagnosticsSink> sinks) {
		public void ReportAll(in DiagnosticEvent d) {
			List<Exception>? exceptions = null;
			foreach (IDiagnosticsSink sink in sinks)
				try {
					sink.Report(in d);
				} catch (Exception ex) {
					(exceptions ??= new List<Exception>()).Add(ex);
				}
			if (exceptions is not null)
				throw new AggregateException(exceptions);
		}
	}
}

public sealed class CompositeDiagnosticsSink(params IDiagnosticsSink[] sinks) : IDiagnosticsSink {
	private readonly IDiagnosticsSink[] sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
	public void Report(in DiagnosticEvent d) => sinks.ReportAll(in d);
}

public sealed class DiagnosticsSinkRegistration : IReloadTeardown {
	private DiagnosticsSinkRegistry? registry;
	private IDiagnosticsSink? sink;
	private int disposed = 0;

	internal DiagnosticsSinkRegistration(DiagnosticsSinkRegistry registry, IDiagnosticsSink sink) {
		this.registry = registry;
		this.sink = sink;
	}

	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public void Remove() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		if (registry is not null && sink is not null)
			registry.Remove(sink);
		registry = null;
		sink = null;
	}

	public void Teardown(in ReloadTeardownContext ctx) => Remove();
}

public sealed class DiagnosticsSinkRegistry(IEnumerable<IDiagnosticsSink> initial) : IDiagnosticsSink {
	private readonly Lock registryLock = new();
	private IDiagnosticsSink[] sinks = initial.ToArray();

	public IReadOnlyList<IDiagnosticsSink> Sinks => Volatile.Read(ref sinks);
	void IDiagnosticsSink.Report(in DiagnosticEvent d) => Sinks.ReportAll(in d);

	public DiagnosticsSinkRegistration Add(IDiagnosticsSink sink) {
		ArgumentNullException.ThrowIfNull(sink);
		lock (registryLock) {
			IDiagnosticsSink[] old = sinks;
			var @new = new IDiagnosticsSink[old.Length + 1];
			Array.Copy(old, @new, old.Length);
			@new[^1] = sink;
			Volatile.Write(ref sinks, @new);
		}
		return new DiagnosticsSinkRegistration(this, sink);
	}

	internal void Remove(IDiagnosticsSink sink) {
		lock (registryLock) {
			IDiagnosticsSink[] old = sinks;
			int idx = Array.IndexOf(old, sink);
			if (idx < 0)
				return;

			var @new = new IDiagnosticsSink[old.Length - 1];
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
