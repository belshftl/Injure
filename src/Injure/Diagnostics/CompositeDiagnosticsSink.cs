// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods;
using Injure.Mods.CodeAnalysis;

namespace Injure.Diagnostics;

/// <summary>
/// Combines multiple diagnostics sinks into a single sink using
/// <see cref="DiagnosticsSinkExtensions.ReportAll"/>; see the documentation of that method for
/// behavior details.
/// </summary>
/// <param name="sinks">The sinks to combine.</param>
/// <remarks>
/// <paramref name="sinks"/> is copied during construction; later changes to the original array do
/// not affect this sink.
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
