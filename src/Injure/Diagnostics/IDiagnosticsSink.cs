// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods;

namespace Injure.Diagnostics;

/// <summary>
/// Receives diagnostic events.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be thread-safe, including support for concurrent
/// <see cref="Report(in DiagnosticEvent)"/> calls.
/// </para>
/// <para>
/// Messages may contain formatting intended for monospaced presentation, such as indentation,
/// aligned columns, and source excerpts. A sink that controls final presentation should use a
/// monospaced font where practical. A sink that forwards diagnostics elsewhere is not responsible
/// for enforcing this.
/// </para>
/// </remarks>
public interface IDiagnosticsSink {
	/// <summary>
	/// Reports a diagnostic event.
	/// </summary>
	void Report(in DiagnosticEvent d);
}

/// <summary>
/// Provides utility/convenience methods for <see cref="IDiagnosticsSink"/> or collections of
/// diagnostics sinks.
/// </summary>
public static class DiagnosticsSinkExtensions {
	extension(IEnumerable<IDiagnosticsSink> sinks) {
		/// <summary>
		/// Reports a diagnostic event to every sink in the sequence.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Sink exceptions are collected in enumeration order and rethrown as an
		/// <see cref="AggregateException"/> after all sinks have been invoked.
		/// </para>
		/// <para>
		/// The sequence is enumerated exactly once. This method does not provide synchronization for the
		/// sequence or its elements.
		/// </para>
		/// <para>
		/// A <see langword="null"/> element records an <see cref="ArgumentNullException"/> in the same
		/// way as a sink-thrown exception.
		/// </para>
		/// <para>
		/// Exceptions thrown by enumeration itself are not treated as sink exceptions and are not caught;
		/// as such, they are thrown out of the method, terminating reporting.
		/// </para>
		/// </remarks>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="sinks"/> is <see langword="null"/>.
		/// </exception>
		/// <exception cref="AggregateException">
		/// Thrown after reporting completes if one or more sink invocations failed, or if the sequence
		/// contained a <see langword="null"/> element.
		/// </exception>
		public void ReportAll(in DiagnosticEvent d) {
			ArgumentNullException.ThrowIfNull(sinks);
			List<Exception>? exceptions = null;
			foreach (IDiagnosticsSink sink in sinks)
				try {
#pragma warning disable CA2208 // method '...' passes '...' as the paramName argument to ArgumentNullException; replace it with ...
					if (sink is null)
						throw new ArgumentNullException(nameof(sinks), "enumerable contains a null sink value");
#pragma warning restore CA2208 // method '...' passes '...' as the paramName argument to ArgumentNullException; replace it with ...
					sink.Report(in d);
				} catch (Exception ex) when (!ExceptionPolicy.IsInternalState(ex)) {
					(exceptions ??= new List<Exception>()).Add(ex);
				}
			if (exceptions is not null)
				throw new AggregateException(exceptions);
		}
	}

	extension(IDiagnosticsSink sink) {
		/// <summary>
		/// Creates an <see cref="IOwnerDiagnostics"/> that attributes its diagnostics to
		/// <paramref name="ownerId"/> (and <paramref name="generation"/>, if given), and reports them to
		/// this sink.
		/// </summary>
		/// <param name="ownerId">Owner ID of the component emitting the diagnostics.</param>
		/// <param name="generation">
		/// Reload generation of the component, or <see langword="null"/> if it has none.
		/// </param>
		/// <returns>The owner diagnostics; thread-safe if this sink is.</returns>
		/// <exception cref="ArgumentNullException">
		/// Thrown if this sink or <paramref name="ownerId"/> is <see langword="null"/>.
		/// </exception>
		/// <exception cref="ArgumentException">
		/// Thrown if <paramref name="ownerId"/> isn't a valid owner ID, or <paramref name="generation"/>
		/// belongs to a different owner.
		/// </exception>
		public IOwnerDiagnostics ForOwner(string ownerId, ReloadGeneration? generation = null) {
			ArgumentNullException.ThrowIfNull(sink);
			ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
			if (generation is ReloadGeneration g && g.OwnerId != ownerId)
				throw new ArgumentException("the reload generation belongs to a different owner", nameof(generation));
			return new OwnerDiagnostics(ownerId, sink, generation);
		}
	}
}
