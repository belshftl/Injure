// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace Injure.ModKit.Abstractions;

public sealed class ExceptionSnapshot {
	public string BestEffortTypeName { get; }
	public string BestEffortMessage { get; }
	public string BestEffortStackTrace { get; }
	public string BestEffortExceptionText { get; }

	public string? TypeName { get; }
	public string? FullTypeName { get; }
	public string? AssemblyQualifiedTypeName { get; }
	public string? AssemblyName { get; }

	public string? Message { get; }
	public string? StackTrace { get; }
	public string? ExceptionText { get; }

	public int HResult { get; }
	public string? Source { get; }
	public string? HelpLink { get; }

	public IReadOnlyList<ExceptionSnapshot> InnerExceptions { get; }

	private ExceptionSnapshot(
		string? typeName,
		string? fullTypeName,
		string? assemblyQualifiedTypeName,
		string? assemblyName,
		string? message,
		string? stackTrace,
		string? exceptionText,
		int hresult,
		string? source,
		string? helpLink,
		ExceptionSnapshot[] innerExceptions
	) {
		TypeName = typeName;
		FullTypeName = fullTypeName;
		AssemblyQualifiedTypeName = assemblyQualifiedTypeName;
		AssemblyName = assemblyName;

		Message = message;
		StackTrace = stackTrace;
		ExceptionText = exceptionText;

		HResult = hresult;
		Source = source;
		HelpLink = helpLink;

		InnerExceptions = innerExceptions;

		BestEffortTypeName = !string.IsNullOrWhiteSpace(FullTypeName) ? FullTypeName : !string.IsNullOrWhiteSpace(TypeName) ? TypeName : "<unknown exception type>";
		BestEffortMessage = Message ?? "<message unavailable>";
		BestEffortStackTrace = StackTrace ?? "<stack trace unavailable>";
		BestEffortExceptionText = ExceptionText ?? $"{BestEffortTypeName}: {BestEffortMessage}\n{BestEffortStackTrace}";
	}

	public static ExceptionSnapshot FromException(Exception ex) {
		ArgumentNullException.ThrowIfNull(ex);
		ExceptionPolicy.ThrowIfInternalState(ex);
		Type type = ex.GetType();
		return new ExceptionSnapshot(
			typeName: type.Name,
			fullTypeName: type.FullName,
			assemblyQualifiedTypeName: type.AssemblyQualifiedName,
			assemblyName: type.Assembly.GetName().Name,
			message: catchEx(() => ex.Message, null),
			stackTrace: catchEx(() => ex.StackTrace, null),
			exceptionText: catchEx(() => ex.ToString(), null),
			hresult: catchEx(() => ex.HResult, 0),
			source: catchEx(() => ex.Source, null),
			helpLink: catchEx(() => ex.HelpLink, null),
			innerExceptions: captureInnerExceptions(ex)
		);
	}

	private static T? catchEx<T>(Func<T?> read, T? fallback) {
		try {
			return read();
		} catch {
			return fallback;
		}
	}

	public ForeignException ToException() => new(this);

	public override string ToString() => BestEffortExceptionText;

	public bool IsType(string fullName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
		return FullTypeName == fullName;
	}
	public bool IsType(Type type) {
		ArgumentNullException.ThrowIfNull(type);
		if (AssemblyQualifiedTypeName is not null && type.AssemblyQualifiedName is not null)
			return AssemblyQualifiedTypeName == type.AssemblyQualifiedName;
		return FullTypeName == type.FullName;
	}
	public bool IsType<T>() where T : Exception => IsType(typeof(T));

	public ExceptionSnapshot UnwrapUnary(Func<ExceptionSnapshot, bool> shouldUnwrap) {
		ArgumentNullException.ThrowIfNull(shouldUnwrap);
		ExceptionSnapshot curr;
		for (curr = this; curr.InnerExceptions.Count == 1 && shouldUnwrap(curr); curr = curr.InnerExceptions[0]) ;
		return curr;
	}

	public IEnumerable<ExceptionSnapshot> SelfAndDescendants() {
		Stack<ExceptionSnapshot> stack = new();
		stack.Push(this);
		while (stack.TryPop(out ExceptionSnapshot? curr)) {
			yield return curr;
			for (int i = curr.InnerExceptions.Count - 1; i >= 0; i--)
				stack.Push(curr.InnerExceptions[i]);
		}
	}
	public ExceptionSnapshot[] Leaves() => SelfAndDescendants().Where(static e => e.InnerExceptions.Count == 0).ToArray();

	private static ExceptionSnapshot[] captureInnerExceptions(Exception ex) {
		if (ex is AggregateException agg)
			return agg.InnerExceptions.Select(static e => FromException(e)).ToArray();
		if (ex.InnerException is not Exception innerException)
			return Array.Empty<ExceptionSnapshot>();
		return [FromException(innerException)];
	}
}
