// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods;
using Injure.Mods.CodeAnalysis;

namespace Injure.Diagnostics;

/// <summary>
/// Handle to a registered diagnostics sink in a <see cref="DiagnosticsSinkRegistry"/>, used for
/// unregistration.
/// </summary>
public sealed class DiagnosticsSinkRegistration : IReloadTeardown {
	private DiagnosticsSinkRegistry? registry;
	private readonly ulong id;
	private int removed = 0;

	internal DiagnosticsSinkRegistration(DiagnosticsSinkRegistry registry, ulong id) {
		this.registry = registry;
		this.id = id;
	}

	/// <summary>
	/// Removes the registration. No-op if it has already been removed.
	/// </summary>
	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public void Remove() {
		if (Interlocked.Exchange(ref removed, 1) != 0)
			return;
		registry?.Remove(id);
		registry = null;
	}

	/// <summary>
	/// <see cref="IReloadTeardown"/> implementation; equivalent to <see cref="Remove()"/>.
	/// </summary>
	void IReloadTeardown.Teardown(in ReloadTeardownCtx ctx) => Remove();
}

/// <summary>
/// Maintains a thread-safe, dynamically replaceable collection of diagnostic sinks.
/// </summary>
/// <remarks>
/// <para>
/// Reporting uses immutable array snapshots. Adding or removing a sink does not block reports
/// already in progress, and a report observes one complete registry snapshot.
/// </para>
/// <para>
/// A sink added during a report is not included in that report. A sink removed during a report may
/// still receive that report if it was present in the snapshot already obtained by the reporting
/// thread.
/// </para>
/// <para>
/// Diagnostics are reported to sinks using <see cref="DiagnosticsSinkExtensions.ReportAll"/>; see
/// the documentation of that method for behavior details.
/// </para>
/// <para>
/// Registry operations are thread-safe. Registered sinks may be invoked concurrently by different
/// reporting threads and should themselves be thread-safe.
/// </para>
/// </remarks>
public sealed class DiagnosticsSinkRegistry : IDiagnosticsSink {
	private readonly Lock registryLock = new();
	private ulong nextSinkID = 0;
	private ImmutableDictionary<ulong, IDiagnosticsSink> sinks;

	/// <summary>
	/// Creates a new <see cref="DiagnosticsSinkRegistry"/> with the given initial set of registered
	/// sinks.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="initial"/> is <see langword="null"/> or contains a
	/// <see langword="null"/> element.
	/// </exception>
	public DiagnosticsSinkRegistry(IEnumerable<IDiagnosticsSink> initial) {
		ArgumentNullException.ThrowIfNull(initial);
		Dictionary<ulong, IDiagnosticsSink> dict = new();
		foreach (IDiagnosticsSink sink in initial) {
			ArgumentNullException.ThrowIfNull(sink);
			dict.Add(checked(nextSinkID++), sink);
		}
		sinks = dict.ToImmutableDictionary();
	}

	/// <summary>
	/// Gets a snapshot of the currently registered diagnostic sinks, enumerated in an indeterminate
	/// order. Later registry changes do not affect the returned sequence.
	/// </summary>
	public IEnumerable<IDiagnosticsSink> Sinks => Volatile.Read(ref sinks).Values;
	void IDiagnosticsSink.Report(in DiagnosticEvent d) => Sinks.ReportAll(in d);

	/// <summary>
	/// Adds a diagnostic sink to the registry.
	/// </summary>
	/// <returns>
	/// A registration handle that can be used to remove the registration.
	/// </returns>
	/// <remarks>
	/// Adding the same sink instance more than once creates independent registrations; no attempt to
	/// deduplicate/coalesce entries or otherwise check them for equality is made.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="sink"/> is <see langword="null"/>.
	/// </exception>
	public DiagnosticsSinkRegistration Add(IDiagnosticsSink sink) {
		ArgumentNullException.ThrowIfNull(sink);
		lock (registryLock) {
			ulong id = checked(nextSinkID++);
			ImmutableDictionary<ulong, IDiagnosticsSink> old = Volatile.Read(ref sinks);
			ImmutableDictionary<ulong, IDiagnosticsSink> @new = old.Add(id, sink);
			Volatile.Write(ref sinks, @new);
			return new DiagnosticsSinkRegistration(this, id);
		}
	}

	internal void Remove(ulong id) {
		lock (registryLock) {
			ImmutableDictionary<ulong, IDiagnosticsSink> old = Volatile.Read(ref sinks);
			ImmutableDictionary<ulong, IDiagnosticsSink> @new = old.Remove(id);
			Volatile.Write(ref sinks, @new);
		}
	}
}
