// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Injure.Mods;

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
/// As an example of special treatment, <see cref="ExceptionSnapshot"/> will refuse
/// to wrap this exception type and simply re-throws it instead.
/// </para>
/// </remarks>
public sealed class InternalStateException : Exception {
	internal InternalStateException() {}
	internal InternalStateException(string message) : base(message) {}
	internal InternalStateException(string message, Exception ex) : base(message, ex) {}

	internal static void ThrowIfNull<T>(
		T? v,
		[CallerArgumentExpression(nameof(v))] string? expr = null,
		[CallerFilePath] string file = "<unknown>",
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = "<unknown>"
	) where T : class {
		if (v is null)
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' is unexpectedly null");
	}

#pragma warning disable IDE0001 // name can be simplified
	internal static void ThrowIfNull<T>(
		Nullable<T> v,
		[CallerArgumentExpression(nameof(v))] string? expr = null,
		[CallerFilePath] string file = "<unknown>",
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = "<unknown>"
	) where T : struct {
		if (v is null)
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' is unexpectedly null");
	}
#pragma warning restore IDE0001 // name can be simplified

	internal static void ThrowIfNullOrEmpty(
		string? s,
		[CallerArgumentExpression(nameof(s))] string? expr = null,
		[CallerFilePath] string file = "<unknown>",
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = "<unknown>"
	) {
		if (s is null)
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' is unexpectedly null");
		if (s.Length == 0)
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' is unexpectedly empty");
	}

	internal static void ThrowIfNullOrWhiteSpace(
		string? s,
		[CallerArgumentExpression(nameof(s))] string? expr = null,
		[CallerFilePath] string file = "<unknown>",
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = "<unknown>"
	) {
		if (s is null)
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' is unexpectedly null");
		if (s.Length == 0)
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' is unexpectedly empty");
		if (s.All(char.IsWhiteSpace))
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' is unexpectedly whitespace-only");
	}

	internal static void ThrowIfInvalidOwnerId(
		string? ownerId,
		[CallerArgumentExpression(nameof(ownerId))] string? expr = null,
		[CallerFilePath] string file = "<unknown>",
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = "<unknown>"
	) {
		if (!ModMetadataValidation.ValidateOwnerId(ownerId, out string? e))
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' (value: '{ownerId}') is unexpectedly not a valid owner ID: {e}");
	}

	internal static void ThrowIfInvalidLocalId(
		string? localId,
		[CallerArgumentExpression(nameof(localId))] string? expr = null,
		[CallerFilePath] string file = "<unknown>",
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = "<unknown>"
	) {
		if (!ModMetadataValidation.ValidateLocalId(localId, out string? e))
			throw new InternalStateException($"{file}:{line}: {member}: '{expr}' (value: '{localId}') is unexpectedly not a valid local ID: {e}");
	}
}

internal static class ExceptionPolicy {
	public static bool IsInternalState(Exception ex) => TryGetInternalStateException(ex, out _);

	public static bool TryGetInternalStateException(Exception ex, [NotNullWhen(true)] out InternalStateException? internalEx) {
		switch (ex) {
		case InternalStateException ise:
			internalEx = ise;
			return true;
		case TargetInvocationException { InnerException: {} inner }:
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
