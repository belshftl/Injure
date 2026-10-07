// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.ClosedFlags;

internal static class ClosedFlagsModel {
	public static readonly ClosedTypeSpec Spec = new() {
		Name = "ClosedFlags",
		NestedEnumName = "Bits",
		RequiresFlagsEnum = true,
		FlagsEnumMismatchMessage = "ClosedFlags Bits enum must be marked with [Flags].",
		MirrorAttributeMetadataName = Constants.ClosedFlags.MirrorAttributeMetadataName,
		ReservedMemberNames = Constants.ClosedFlags.ReservedMemberNames,
		InvalidTarget = Diagnostics.ClosedFlags.ClosedFlagsInvalidTarget,
		MustBeReadonly = Diagnostics.ClosedFlags.ClosedFlagsMustBeReadonly,
		InvalidSourceShape = Diagnostics.ClosedFlags.ClosedFlagsInvalidSourceShape,
		InvalidNestedEnum = Diagnostics.ClosedFlags.ClosedFlagsInvalidBitsEnum,
		AliasNotSupported = Diagnostics.ClosedFlags.ClosedFlagsAliasNotSupported,
		DefaultRule = Diagnostics.ClosedFlags.ClosedFlagsDefaultRule,
		SuspiciousZeroName = Diagnostics.ClosedFlags.ClosedFlagsSuspiciousZeroName,
		MirrorInvalid = Diagnostics.ClosedFlags.ClosedFlagsMirrorInvalid,
	};

	private static bool isAtomic(ulong v) => v != 0 && (v & v - 1) == 0;

	// returns null if any error was reported
	public static ClosedTypeShape? Validate(INamedTypeSymbol sym, AttributeData attr, DiagnosticSink sink, CancellationToken ct) {
		ClosedTypeShape? shape = ClosedType.Validate(Spec, sym, attr, sink, ct);
		if (shape is null)
			return null;

		ulong atomicMask = 0;
		foreach (ClosedTypeMember m in shape.Members)
			if (isAtomic(m.Value))
				atomicMask |= m.Value;
		foreach (ClosedTypeMember m in shape.Members)
			if (m.Value != 0 && (m.Value & ~atomicMask) != 0)
				sink.Report(Diagnostics.ClosedFlags.ClosedFlagsBadMemberValue, Util.GetLocation(m.Field, shape.NestedEnum), m.Field.Name);
		if (atomicMask == 0 && shape.Members.Length != 0)
			sink.Report(
				Diagnostics.ClosedFlags.ClosedFlagsInvalidBitsEnum,
				Util.GetPrimaryLocation(shape.NestedEnum),
				"Bits enum must declare at least one single-bit member."
			);

		foreach (ClosedTypeMirror mirror in shape.Mirrors) {
			ulong externalMask = 0;
			foreach (ulong v in ClosedType.GetEnumValues(mirror.Enum))
				externalMask |= v;
			string external = mirror.Enum.ToDisplayString();
			if ((atomicMask & ~externalMask) != 0)
				sink.Report(
					Diagnostics.ClosedFlags.ClosedFlagsMirrorMismatch,
					mirror.Location,
					$"ClosedFlagsMirror '{sym.Name}.Bits' has one or more bits not present in the target enum ('{external}')."
				);
			if (!mirror.Subset && (externalMask & ~atomicMask) != 0)
				sink.Report(
					Diagnostics.ClosedFlags.ClosedFlagsMirrorMismatch,
					mirror.Location,
					$"ClosedFlagsMirror target enum ('{external}') has one or more bits not present in '{sym.Name}.Bits'; if this is intentional, consider using 'Subset = true'."
				);
		}
		return sink.HasErrors ? null : shape;
	}
}
