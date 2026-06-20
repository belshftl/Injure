// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using Injure.Internals.Analyzers.Attributes;
using Injure.ModKit.Abstractions.CodeAnalysis;

namespace Injure.ModKit.Abstractions;

/// <summary>
/// Describes the severity of a diagnostic.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DiagnosticSeverity {
	/// <summary>Raw switch tag for <see cref="DiagnosticSeverity"/>.</summary>
	public enum Case {
		/// <summary>
		/// The diagnostic is for debugging, tracing, or other verbose operational detail;
		/// the lowest severity level.
		/// </summary>
		Debug = 1,

		/// <summary>
		/// The diagnostic reports normal operation, status, or other information that does
		/// not indicate a problem requiring attention.
		/// </summary>
		Info,

		/// <summary>
		/// The diagnostic reports a potentially problematic condition that does not prevent
		/// continued operation, but may warrant investigation or corrective action.
		/// </summary>
		Warning,

		/// <summary>
		/// The diagnostic reports a non-fatal failure or invalid condition. The affected
		/// operation, component, or requested functionality did not complete successfully
		/// and typically requires corrective action.
		/// </summary>
		/// <remarks>
		/// Conditions that prevent the current operation from continuing should generally
		/// be represented by an exception as well as, or instead of, an error diagnostic.
		/// Fatal runtime or process failures should normally be reported through the
		/// mechanism responsible for handling that failure.
		/// </remarks>
		Error,
	}
}

/// <summary>
/// Describes one diagnostic event emitted by the engine, the game, or a mod.
/// </summary>
/// <param name="SourceOwnerID">Owner ID of the component that emitted the diagnostic.</param>
/// <param name="Severity">Severity of the diagnostic.</param>
/// <param name="Message">Diagnostic message, as reported by the component.</param>
/// <param name="Generation">
/// Reload generation of the component that emitted the diagnostic, or <see langword="null"/>
/// if the diagnostic was emitted by a component that is not associated with a reload generation.
/// </param>
/// <remarks>
/// <see langword="default"/> produces an invalid value of this type.
/// </remarks>
public readonly record struct DiagnosticEvent(
	string SourceOwnerID,
	DiagnosticSeverity Severity,
	string Message,
	ReloadGeneration? Generation = null
) {
	/// <summary>
	/// Throws if this <see cref="DiagnosticEvent"/> is invalid/malformed.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <see cref="SourceOwnerID"/> or <see cref="Message"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <see cref="SourceOwnerID"/> is not a valid owner ID, or if
	/// <see cref="Generation"/> is non-null and has a mismatched owner ID.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Severity"/> is not a validly constructed closed-enum value.
	/// </exception>
	public void Validate() {
		ModMetadataValidation.ValidateOwnerIDOrThrow(SourceOwnerID);
		ArgumentNullException.ThrowIfNull(Message);
		if (Generation is ReloadGeneration g && g.OwnerID != SourceOwnerID)
			throw new ArgumentException("SourceOwnerID doesn't match reload generation owner");
		_ = Severity.Tag;
	}
}

/// <summary>
/// Receives diagnostic events.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be thread-safe, including support for concurrent
/// <see cref="Report(in DiagnosticEvent)"/> calls.
/// </para>
/// <para>
/// Messages may contain formatting intended for monospaced presentation, such as
/// indentation, aligned columns, and source excerpts. A sink that controls final
/// presentation should use a monospaced font where practical. A sink that forwards
/// diagnostics elsewhere is not responsible for enforcing this.
/// </para>
/// </remarks>
public interface IDiagnosticsSink {
	/// <summary>
	/// Reports a diagnostic event.
	/// </summary>
	void Report(in DiagnosticEvent d);
}

/// <summary>
/// Provides utility/convenience methods for <see cref="IDiagnosticsSink"/> or collections
/// of diagnostics sinks.
/// </summary>
public static class DiagnosticsSinkExtensions {
	extension(IEnumerable<IDiagnosticsSink> sinks) {
		/// <summary>
		/// Reports a diagnostic event to every sink in the sequence.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Sink exceptions are collected in enumeration order and rethrown as an
		/// <see cref="AggregateException"/> after all sinks have been invoked,
		/// subject to the standard exception-recording rules; see
		/// <c>Docs/exception-recording.md</c> for more info.
		/// </para>
		/// <para>
		/// The sequence is enumerated exactly once. This method does not provide
		/// synchronization for the sequence or its elements.
		/// </para>
		/// <para>
		/// A <see langword="null"/> element records an <see cref="ArgumentNullException"/>
		/// in the same way as a sink-thrown exception.
		/// </para>
		/// <para>
		/// Exceptions thrown by enumeration itself are not treated as sink exceptions and
		/// are not caught; as such, they are thrown out of the method, terminating reporting.
		/// </para>
		/// </remarks>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="sinks"/> is <see langword="null"/>.
		/// </exception>
		/// <exception cref="AggregateException">
		/// Thrown after reporting completes if one or more sink invocations failed or the
		/// sequence contained a <see langword="null"/> element.
		/// </exception>
		public void ReportAll(in DiagnosticEvent d) {
			ArgumentNullException.ThrowIfNull(sinks);
			List<Exception>? exceptions = null;
			foreach (IDiagnosticsSink sink in sinks) {
				try {
#pragma warning disable CA2208 // method '...' passes '...' as the paramName argument to ArgumentNullException; replace it with ...
					if (sink is null)
						throw new ArgumentNullException(nameof(sinks), "enumerable contains a null sink value");
#pragma warning restore CA2208 // method '...' passes '...' as the paramName argument to ArgumentNullException; replace it with ...
					sink.Report(in d);
				} catch (Exception ex) when (!ExceptionPolicy.IsInternalState(ex)) {
					(exceptions ??= new List<Exception>()).Add(ex);
				}
			}
			if (exceptions is not null)
				throw new AggregateException(exceptions);
		}
	}
}

/// <summary>
/// Combines multiple diagnostics sinks into a single sink using
/// <see cref="DiagnosticsSinkExtensions.ReportAll"/>; see the documentation of
/// that method for behavior details.
/// </summary>
/// <param name="sinks">
/// The sinks to combine.
/// </param>
/// <remarks>
/// <paramref name="sinks"/> is copied during construction; later changes to the
/// original array do not affect this sink.
/// </remarks>
/// <exception cref="ArgumentNullException">
/// Thrown if <paramref name="sinks"/> is <see langword="null"/> or contains a
/// <see langword="null"/> element.
/// </exception>
public sealed class CompositeDiagnosticsSink(params IDiagnosticsSink[] sinks) : IDiagnosticsSink {
	private readonly IDiagnosticsSink[] sinks = sinks?.Select(
		static s => s ?? throw new ArgumentNullException(nameof(sinks), "sink array contains a null sink value")
	)?.ToArray() ?? throw new ArgumentNullException(nameof(sinks));
	public void Report(in DiagnosticEvent d) => sinks.ReportAll(in d);
}

/// <summary>
/// Handle to a registered diagnostics sink in a <see cref="DiagnosticsSinkRegistry"/>, used
/// for unregistration.
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
	public void Teardown(in ReloadTeardownContext ctx) => Remove();
}

/// <summary>
/// Maintains a thread-safe, dynamically replaceable collection of diagnostic sinks.
/// </summary>
/// <remarks>
/// <para>
/// Reporting uses immutable array snapshots. Adding or removing a sink does not
/// block reports already in progress, and a report observes one complete registry
/// snapshot.
/// </para>
/// <para>
/// A sink added during a report is not included in that report. A sink removed
/// during a report may still receive that report if it was present in the snapshot
/// already obtained by the reporting thread.
/// </para>
/// <para>
/// Diagnostics are reported to sinks using <see cref="DiagnosticsSinkExtensions.ReportAll"/>;
/// see the documentation of that method for behavior details.
/// </para>
/// <para>
/// Registry operations are thread-safe. Registered sinks may be invoked concurrently
/// by different reporting threads and should themselves be thread-safe.
/// </para>
/// </remarks>
public sealed class DiagnosticsSinkRegistry : IDiagnosticsSink {
	private readonly Lock registryLock = new();
	private ulong nextSinkID = 0;
	private ImmutableDictionary<ulong, IDiagnosticsSink> sinks;

	/// <summary>
	/// Creates a new <see cref="DiagnosticsSinkRegistry"/> with the given initial
	/// set of registered sinks.
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
	/// Gets a snapshot of the currently registered diagnostic sinks, enumerated in an
	/// indeterminate order. Later registry changes do not affect the returned sequence.
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
	/// Adding the same sink instance more than once creates independent registrations;
	/// no attempt to deduplicate/coalesce entries or otherwise check them for equality is made.
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

/// <summary>
/// Emits diagnostics attributed to one owner.
/// </summary>
/// <remarks>
/// <para>
/// Messages may be targeted towards monospace presentation; see <see cref="IDiagnosticsSink"/>
/// for details.
/// </para>
/// <para>
/// Expected to be implemented by the engine, not a custom implementation. Custom implementations
/// are to attribute every diagnostic to one source owner and, if applicable, reload generation,
/// and forward it to an <see cref="IDiagnosticsSink"/>. Custom implementations must also be thread-safe.
/// </para>
/// </remarks>
public interface IOwnerDiagnostics {
	/// <summary>
	/// Emits a diagnostic with the specified severity.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="message"/> is <see langword="null"/>.
	/// </exception>
	void Log(DiagnosticSeverity severity, string message);

	/// <summary>
	/// Emits a diagnostic with <see cref="DiagnosticSeverity.Debug"/> severity.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="message"/> is <see langword="null"/>.
	/// </exception>
	void Debug(string message);

	/// <summary>
	/// Emits a diagnostic with <see cref="DiagnosticSeverity.Info"/> severity.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="message"/> is <see langword="null"/>.
	/// </exception>
	void Info(string message);

	/// <summary>
	/// Emits a diagnostic with <see cref="DiagnosticSeverity.Warning"/> severity.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="message"/> is <see langword="null"/>.
	/// </exception>
	void Warning(string message);

	/// <summary>
	/// Emits a diagnostic with <see cref="DiagnosticSeverity.Error"/> severity.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="message"/> is <see langword="null"/>.
	/// </exception>
	void Error(string message);
}
