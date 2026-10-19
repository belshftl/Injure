// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using Injure.Mods;

namespace Injure.Diagnostics;

/// <summary>
/// Writes diagnostics as lines of text, with ANSI colors if wanted, e.g. to the standard error
/// stream.
/// </summary>
/// <param name="output">
/// Where to write; writes are serialized, so it doesn't need to be thread-safe.
/// </param>
/// <param name="colorOutput">Whether to color the output with ANSI escape sequences.</param>
/// <param name="minimumSeverity">
/// The least severe diagnostics written, or <see langword="null"/> to write all of them.
/// </param>
/// <remarks>
/// Writes diagnostics of every severity by default, including debug; a game may want to set
/// <paramref name="minimumSeverity"/> to <see cref="DiagnosticSeverity.Info"/> in release builds.
/// </remarks>
public sealed class DefaultDiagnosticsSink(TextWriter output, bool colorOutput, DiagnosticSeverity? minimumSeverity = null) : IDiagnosticsSink {
	private readonly TextWriter output = output ?? throw new ArgumentNullException(nameof(output));
	private readonly bool colorOutput = colorOutput;
	private readonly int minimum = minimumOf(minimumSeverity);
	private readonly Lock outputLock = new(); // XXX: might have contention under very high-volume logging, fine for now

	/// <summary>
	/// Creates a sink writing to the standard error stream, colored unless it's redirected.
	/// </summary>
	/// <param name="minimumSeverity">
	/// The least severe diagnostics written, or <see langword="null"/> to write all of them.
	/// </param>
	public DefaultDiagnosticsSink(DiagnosticSeverity? minimumSeverity = null) : this(Console.Error, colorOutput: !Console.IsErrorRedirected, minimumSeverity) {
	}

	public const string TimestampColor = "\x1b[2m";
	public const string GenerationColor = "\x1b[2m";
	public const string DebugColor = "\x1b[1;37m";
	public const string InfoColor = "\x1b[1;34m";
	public const string WarningColor = "\x1b[1;33m";
	public const string ErrorColor = "\x1b[1;31m";
	public const string OwnerColor = "\x1b[1m";
	private const string resetColor = "\x1b[0m";

	/// <inheritdoc/>
	private static int minimumOf(DiagnosticSeverity? minimumSeverity) {
		ArgumentInvalidDefaultException.ThrowIfDefault(minimumSeverity, nameof(minimumSeverity));
		return (int)(minimumSeverity ?? DiagnosticSeverity.Debug).Tag;
	}

	public void Report(in DiagnosticEvent d) {
		ArgumentInvalidDefaultException.ThrowIfDefault(d);
		if ((int)d.Severity.Tag < minimum)
			return;
		DateTimeOffset now = DateTimeOffset.Now;
		string timestamp = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + now.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "");
		string genText = d.Generation?.Value.ToString("D4") ?? "XXXX";
		(string severityColor, string severityMarker) = d.Severity.Tag switch {
			DiagnosticSeverity.Case.Debug => (DebugColor, "[DEBUG]"),
			DiagnosticSeverity.Case.Info => (InfoColor, "[INFO]"),
			DiagnosticSeverity.Case.Warning => (WarningColor, "[WARN]"),
			DiagnosticSeverity.Case.Error => (ErrorColor, "[ERROR]"),
			_ => throw new UnreachableException(),
		};
		lock (outputLock) {
			if (colorOutput)
				output.WriteLine(
					$"{TimestampColor}({timestamp}){resetColor} {OwnerColor}[{d.SourceOwnerId}{resetColor}{GenerationColor}@{genText}{resetColor}{OwnerColor}]{resetColor} {severityColor}{severityMarker}{resetColor} {d.Message}"
				);
			else
				output.WriteLine($"({timestamp}) [{d.SourceOwnerId}@{genText}] {severityMarker} {d.Message}");
		}
	}
}

internal sealed class OwnerDiagnostics : IOwnerDiagnostics {
	private readonly string ownerId;
	private readonly IDiagnosticsSink sink;
	private readonly ReloadGeneration? generation;

	internal OwnerDiagnostics(string ownerId, IDiagnosticsSink sink, ReloadGeneration? generation) {
		this.ownerId = ownerId;
		this.sink = sink;
		this.generation = generation;
	}

	public void Log(DiagnosticSeverity severity, string message) => sink.Report(new DiagnosticEvent(ownerId, severity, message, generation));
	public void Debug(string message) => sink.Report(new DiagnosticEvent(ownerId, DiagnosticSeverity.Debug, message, generation));
	public void Info(string message) => sink.Report(new DiagnosticEvent(ownerId, DiagnosticSeverity.Info, message, generation));
	public void Warning(string message) => sink.Report(new DiagnosticEvent(ownerId, DiagnosticSeverity.Warning, message, generation));
	public void Error(string message) => sink.Report(new DiagnosticEvent(ownerId, DiagnosticSeverity.Error, message, generation));
}
