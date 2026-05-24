// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace Injure.ModKit;

public readonly record struct ExceptionSnapshot(string TypeName, string Message, string? StackTrace = null, bool WasTargetInvocationExceptionWrapped = false) {
	public override string ToString() => !WasTargetInvocationExceptionWrapped
		? $"{TypeName}: {Message}\n{StackTrace ?? "<stack trace unavailable>"}"
		: $"TargetInvocationException: {TypeName}: {Message}\n{StackTrace ?? "<stack trace unavailable>"}";
	public static ExceptionSnapshot FromException(Exception ex) {
		if (ex is TargetInvocationException { InnerException: { } inner })
			return new ExceptionSnapshot(inner.GetType()?.FullName ?? inner.GetType().Name, inner.Message, inner.StackTrace, WasTargetInvocationExceptionWrapped: true);
		return new ExceptionSnapshot(ex.GetType()?.FullName ?? ex.GetType().Name, ex.Message, ex.StackTrace);
	}
	public ForeignException ToException() => new(TypeName, Message, StackTrace, WasTargetInvocationExceptionWrapped);
}
