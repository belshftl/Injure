// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Runtime;

public sealed class DefaultDiagnosticsSink(TextWriter output, bool colorOutput) : IDiagnosticsSink {
	private readonly TextWriter output = output;
	private readonly bool colorOutput = colorOutput;
	private readonly Lock outputLock = new(); // XXX: might have contention under very high-volume logging, fine for now

	public DefaultDiagnosticsSink() : this(Console.Error, colorOutput: !Console.IsErrorRedirected) {
	}

	public const string TimestampColor = "\x1b[0;30m";
	public const string GenerationColor = "\x1b[0;30m";
	public const string DebugColor = "\x1b[1;37m";
	public const string InfoColor = "\x1b[1;34m";
	public const string WarningColor = "\x1b[1;33m";
	public const string ErrorColor = "\x1b[1;31m";
	public const string OwnerColor = "\x1b[1m";
	private const string resetColor = "\x1b[0m";

	public void Report(in DiagnosticEvent d) {
		DateTimeOffset now = DateTimeOffset.Now;
		string timestamp = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + now.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "");
		string genText = d.Generation?.Value.ToString("D4") ?? "XXXX";
		(string severityColor, string severityMarker) = d.Severity.Tag switch {
			DiagnosticSeverity.Case.Debug => (DebugColor, "[DEBUG]"),
			DiagnosticSeverity.Case.Info => (InfoColor, "[INFO]"),
			DiagnosticSeverity.Case.Warning => (WarningColor, "[WARN]"),
			DiagnosticSeverity.Case.Error => (ErrorColor, "[ERROR] "),
			_ => throw new UnreachableException(),
		};
		lock (outputLock) {
			if (colorOutput)
				output.WriteLine($"{TimestampColor}({timestamp}){resetColor} {OwnerColor}[{d.SourceOwnerID}{resetColor}{GenerationColor}@{genText}{resetColor}{OwnerColor}]{resetColor} {severityColor}{severityMarker}{resetColor} {d.Message}");
			else
				output.WriteLine($"({timestamp}) [{d.SourceOwnerID}@{genText}] {severityMarker} {d.Message}");
		}
	}
}

internal sealed class OwnerDiagnostics : IOwnerDiagnostics {
	private readonly string ownerID;
	private readonly IDiagnosticsSink sink;
	private readonly ReloadGeneration? generation;

	internal OwnerDiagnostics(string ownerID, IDiagnosticsSink sink, ReloadGeneration? generation) {
		this.ownerID = ownerID;
		this.sink = sink;
		this.generation = generation;
	}

	public void Log(DiagnosticSeverity severity, string message) => sink.Report(new DiagnosticEvent(ownerID, severity, message, generation));
	public void Debug(string message) => sink.Report(new DiagnosticEvent(ownerID, DiagnosticSeverity.Debug, message, generation));
	public void Info(string message) => sink.Report(new DiagnosticEvent(ownerID, DiagnosticSeverity.Info, message, generation));
	public void Warning(string message) => sink.Report(new DiagnosticEvent(ownerID, DiagnosticSeverity.Warning, message, generation));
	public void Error(string message) => sink.Report(new DiagnosticEvent(ownerID, DiagnosticSeverity.Error, message, generation));
}
