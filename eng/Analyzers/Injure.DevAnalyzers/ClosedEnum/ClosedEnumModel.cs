// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.ClosedEnum;

internal static class ClosedEnumModel {
	public static readonly ClosedTypeSpec Spec = new() {
		Name = "ClosedEnum",
		NestedEnumName = "Case",
		RequiresFlagsEnum = false,
		FlagsEnumMismatchMessage = "ClosedEnum Case enum must not be marked with [Flags]. Use ClosedFlags for flag sets.",
		MirrorAttributeMetadataName = Constants.ClosedEnum.MirrorAttributeMetadataName,
		ReservedMemberNames = Constants.ClosedEnum.ReservedMemberNames,
		InvalidTarget = Diagnostics.ClosedEnum.ClosedEnumInvalidTarget,
		MustBeReadonly = Diagnostics.ClosedEnum.ClosedEnumMustBeReadonly,
		InvalidSourceShape = Diagnostics.ClosedEnum.ClosedEnumInvalidSourceShape,
		InvalidNestedEnum = Diagnostics.ClosedEnum.ClosedEnumInvalidCaseEnum,
		AliasNotSupported = Diagnostics.ClosedEnum.ClosedEnumAliasNotSupported,
		DefaultRule = Diagnostics.ClosedEnum.ClosedEnumDefaultRule,
		SuspiciousZeroName = Diagnostics.ClosedEnum.ClosedEnumSuspiciousZeroName,
		MirrorInvalid = Diagnostics.ClosedEnum.ClosedEnumMirrorInvalid,
	};

	// returns null if any error was reported
	public static ClosedTypeShape? Validate(INamedTypeSymbol sym, AttributeData attr, DiagnosticSink sink, CancellationToken ct) {
		ClosedTypeShape? shape = ClosedType.Validate(Spec, sym, attr, sink, ct);
		if (shape is null)
			return null;

		HashSet<ulong> closedVals = new(shape.Members.Select(static m => m.Value));
		foreach (ClosedTypeMirror mirror in shape.Mirrors) {
			HashSet<ulong> externalVals = new(ClosedType.GetEnumValues(mirror.Enum));
			string external = mirror.Enum.ToDisplayString();
			foreach (ulong v in closedVals)
				if (!externalVals.Contains(v))
					sink.Report(
						Diagnostics.ClosedEnum.ClosedEnumMirrorMismatch,
						mirror.Location,
						$"ClosedEnumMirror has a numeric value '{Util.UInt64Display(v)}' not present in the target enum ('{external}')."
					);
			if (!mirror.Subset)
				foreach (ulong v in externalVals)
					if (!closedVals.Contains(v))
						sink.Report(
							Diagnostics.ClosedEnum.ClosedEnumMirrorMismatch,
							mirror.Location,
							$"ClosedEnumMirror target enum ('{external}') has a numeric value '{Util.UInt64Display(v)}' not present in '{sym.Name}.Case'; if this is intentional, consider using 'Subset = true'."
						);
		}
		return sink.HasErrors ? null : shape;
	}
}
