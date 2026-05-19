// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

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

public interface IOwnerDiagnostics {
	void Debug(string message);
	void Info(string message);
	void Warning(string message);
	void Error(string message);
}
