// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace Injure.ModKit.Abstractions;

public sealed class ForeignException : Exception {
	public ExceptionSnapshot Snapshot { get; }

	public string BestEffortOriginalTypeName => Snapshot.BestEffortTypeName;
	public string BestEffortOriginalMessage => Snapshot.BestEffortMessage;

	public string? OriginalTypeName => Snapshot.TypeName;
	public string? OriginalFullTypeName => Snapshot.FullTypeName;
	public string? OriginalAssemblyQualifiedTypeName => Snapshot.AssemblyQualifiedTypeName;
	public string? OriginalAssemblyName => Snapshot.AssemblyName;
	public string? OriginalMessage => Snapshot.Message;
	public string? OriginalStackTrace => Snapshot.StackTrace;
	public string? OriginalExceptionText => Snapshot.ExceptionText;
	public IReadOnlyList<ExceptionSnapshot> OriginalInnerExceptions => Snapshot.InnerExceptions;

	public ForeignException(ExceptionSnapshot snapshot) : base(createMessage(snapshot), createInnerException(snapshot)) {
		Snapshot = snapshot;
		HResult = snapshot.HResult;
		Source = snapshot.Source;
		HelpLink = snapshot.HelpLink;
	}

	public override string ToString() => Snapshot.ToString();

	private static string createMessage(ExceptionSnapshot snapshot) {
		ArgumentNullException.ThrowIfNull(snapshot);
		return $"{snapshot.BestEffortTypeName}: {snapshot.BestEffortMessage}";
	}

	private static Exception? createInnerException(ExceptionSnapshot snapshot) {
		ArgumentNullException.ThrowIfNull(snapshot);
		if (snapshot.InnerExceptions.Count == 0)
			return null;
		if (snapshot.InnerExceptions.Count == 1)
			return new ForeignException(snapshot.InnerExceptions[0]);
		return new AggregateException(snapshot.InnerExceptions.Select(static e => new ForeignException(e)));
	}
}
