// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Injure;

/// <summary>
/// Exception thrown on internal logic bugs, invariant violations, state corruption,
/// seemingly impossible conditions, bad values for purely-internal types, etc.
/// Informally speaking, you should never see this unless either there's a bug in the library
/// or <c>unsafe</c> code / reflection over internals / mod hooks have messed something up.
/// </summary>
/// <remarks>
/// <para>
/// Do not throw this from your own code; the engine may have special handling for this specific
/// exception type and treat it as a sign of unreliable state, and said special handling may include
/// poisoning objects, skipping error handling / cleanup, bailing out on tasks/operations, or aborting
/// the process; complete freedom on what exactly could happen is reserved.
/// </para>
/// <para>
/// As an example of special treatment, <see cref="ModKit.Abstractions.ExceptionSnapshot"/> will refuse
/// to wrap this exception type and simply re-throws it instead.
/// </para>
/// </remarks>
public sealed class InternalStateException : Exception {
	internal InternalStateException() {}
	internal InternalStateException(string message) : base(message) {}
	internal InternalStateException(string message, Exception ex) : base(message, ex) {}
}

internal static class ExceptionPolicy {
	public static bool IsInternalState(Exception ex) => TryGetInternalStateException(ex, out _);

	public static bool TryGetInternalStateException(Exception ex, [NotNullWhen(true)] out InternalStateException? internalEx) {
		switch (ex) {
		case InternalStateException ise:
			internalEx = ise;
			return true;
		case TargetInvocationException { InnerException: { } inner }:
			return TryGetInternalStateException(inner, out internalEx);
		case AggregateException agg:
			foreach (Exception innerEx in agg.InnerExceptions)
				if (TryGetInternalStateException(innerEx, out internalEx))
					return true;
			break;
		}
		internalEx = null;
		return false;
	}

	public static void ThrowIfInternalState(Exception ex) {
		if (!TryGetInternalStateException(ex, out InternalStateException? ise))
			return;
		ExceptionDispatchInfo.Capture(ise).Throw();
		throw new UnreachableException();
	}
}
