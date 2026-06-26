// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;
using System.Linq;

using Injure.Internals.Analyzers.Attributes;
using Injure.Mods;

namespace Injure.Assets;

/// <summary>
/// Stage of an asset reload operation where a failure occurred.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct AssetReloadFailureStage {
	/// <summary>Raw switch tag for <see cref="AssetReloadFailureStage"/>.</summary>
	public enum Case {
		/// <summary>
		/// The reload failed while preparing the replacement asset version.
		/// </summary>
		Prepare = 1,

		/// <summary>
		/// The reload failed while applying the prepared replacement version.
		/// </summary>
		Finalize,
	}
}

/// <summary>
/// Origin of an asset reload request.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct AssetReloadRequestOrigin {
	/// <summary>Raw switch tag for <see cref="AssetReloadRequestOrigin"/>.</summary>
	public enum Case {
		/// <summary>
		/// The reload was explicitly requested by caller code.
		/// </summary>
		Explicit = 1,

		/// <summary>
		/// The reload was caused by a watcher reporting a change in a published dependency.
		/// </summary>
		Dependency,
	}
}

/// <summary>
/// Describes a <see cref="IAssetDependency"/> value. Used for diagnostics / failure reports
/// to avoid storing <see cref="IAssetDependency"/> objects long-term, potentially rooting collectible ALCs.
/// </summary>
public readonly record struct AssetDependencySnapshot(
	string BestEffortTypeName,
	string BestEffortDebugDescription,
	string? TypeName,
	string? FullTypeName,
	string? AssemblyQualifiedTypeName,
	string? AssemblyName,
	string? DebugDescription
) {
	/// <summary>
	/// Returns a human-readable best-effort description string.
	/// </summary>
	public override string ToString() => $"{BestEffortTypeName}: {BestEffortDebugDescription}";

	/// <summary>
	/// Creates an <see cref="AssetDependencySnapshot"/> from the provided asset dependency.
	/// </summary>
	public static AssetDependencySnapshot FromDependency(IAssetDependency dep) {
		ArgumentNullException.ThrowIfNull(dep);
		Type type = dep.GetType();
		string? debugDescription = catchEx(() => dep.DebugDescription, null);
		string? typeName = catchEx(() => type.Name, null);
		string? fullTypeName = catchEx(() => type.FullName, null);
		string? assemblyQualifiedTypeName = catchEx(() => type.AssemblyQualifiedName, null);
		string? assemblyName = catchEx(() => type.Assembly.GetName().Name, null);
		return new AssetDependencySnapshot(
			BestEffortTypeName: !string.IsNullOrWhiteSpace(fullTypeName) ? fullTypeName : !string.IsNullOrWhiteSpace(typeName) ? typeName : "<unknown dependency type>",
			BestEffortDebugDescription: !string.IsNullOrWhiteSpace(debugDescription) ? debugDescription : "<dependency description unavailable>",
			TypeName: typeName,
			FullTypeName: fullTypeName,
			AssemblyQualifiedTypeName: assemblyQualifiedTypeName,
			AssemblyName: assemblyName,
			DebugDescription: debugDescription
		);
	}

	private static T? catchEx<T>(Func<T?> read, T? fallback) {
		try {
			return read();
		} catch {
			return fallback;
		}
	}
}

/// <summary>
/// Describes a failed asset reload attempt.
/// </summary>
/// <param name="Asset">Asset key identifying the asset whose reload failed.</param>
/// <param name="TargetVersion">Asset version that the failed reload was trying to prepare or publish.</param>
/// <param name="Stage">Stage where the reload failed.</param>
/// <param name="Origin">Origin of the reload request.</param>
/// <param name="Trigger">Dependency that triggered the reload, if any.</param>
/// <param name="ExceptionSnapshot">Exception that caused the reload failure.</param>
/// <remarks>
/// Failed reloads do not replace the currently live version. The old live version remains active
/// until a later reload succeeds or the asset/store is otherwise discarded.
/// </remarks>
public sealed record AssetReloadFailure(
	AssetKey Asset,
	ulong TargetVersion,
	AssetReloadFailureStage Stage,
	AssetReloadRequestOrigin Origin,
	AssetDependencySnapshot? Trigger,
	ExceptionSnapshot ExceptionSnapshot
);

/// <summary>
/// Result of applying queued asset reloads.
/// </summary>
/// <param name="AppliedCount">Number of prepared reloads that were successfully published.</param>
/// <param name="Failures">Finalize failures that occurred during the apply call.</param>
public readonly record struct AssetReloadReport(
	int AppliedCount,
	ImmutableArray<AssetReloadFailure> Failures
) {
	/// <summary>
	/// Throws if this report contains any reload failures.
	/// </summary>
	/// <exception cref="AggregateException">
	/// Thrown when <see cref="Failures"/> contains one or more entries. Contains
	/// all of the <see cref="AssetReloadFailure.ExceptionSnapshot"/>s in them
	/// converted to <see cref="ForeignException"/>s.
	/// </exception>
	public void ThrowIfFailed() {
		if (!Failures.IsDefaultOrEmpty)
			throw new AggregateException(Failures.Select(static f => f.ExceptionSnapshot.ToException()));
	}
}
