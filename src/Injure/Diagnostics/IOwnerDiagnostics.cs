// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Diagnostics;

/// <summary>
/// Emits diagnostics attributed to one owner.
/// </summary>
/// <remarks>
/// <para>
/// Messages may be targeted towards monospace presentation; see <see cref="IDiagnosticsSink"/> for
/// details.
/// </para>
/// <para>
/// Expected to be implemented by the engine, not a custom implementation. Custom implementations
/// are to attribute every diagnostic to one source owner and, if applicable, reload generation, and
/// forward it to an <see cref="IDiagnosticsSink"/>. Custom implementations must also be
/// thread-safe.
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
