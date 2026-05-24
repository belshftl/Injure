// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit;

public sealed class ForeignException(
	string originalTypeName,
	string originalMessage,
	string? originalStackTrace,
	bool wasTargetInvocationExceptionWrapped = false
) : Exception($"{originalTypeName}: {originalMessage}") {
	public string OriginalTypeName { get; } = originalTypeName;
	public string OriginalMessage { get; } = originalMessage;
	public string? OriginalStackTrace { get; } = originalStackTrace;
	public bool WasTargetInvocationExceptionWrapped { get; } = wasTargetInvocationExceptionWrapped;
}
