// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection.Metadata;
using System.Text;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Direction in which a pattern search scanned, used to phrase failure messages.
/// </summary>
internal enum IlSearchDirection : byte {
	Forward,
	Backward,
}

internal static class IlSearchDirectionExtensions {
	extension (IlSearchDirection dir) {
		public string Display => dir switch {
			IlSearchDirection.Forward => "forward",
			IlSearchDirection.Backward => "backward",
			_ => throw InternalStateException.BadOpenEnum(dir),
		};
	}
}

/// <summary>
/// What a failed pattern search was looking for and where it started.
/// </summary>
/// <remarks>
/// This is info for failure messages and should never be constructed on a successful search.
/// Formatting a pattern allocates a multi-line string, so the pattern and provenance constraint info
/// are carried to be lazily formatted later.
/// </remarks>
internal readonly ref struct IlPatternSearchInfo(
	int startInstrBoundary,
	IlSearchDirection direction,
	ReadOnlySpan<IlPatternElement> pattern,
	IlProvenanceConstr provenance,
	IlFormatInfo formatInfo
) {
	public int StartInstructionBoundary { get; } = startInstrBoundary;
	public IlSearchDirection Direction { get; } = direction;
	public ReadOnlySpan<IlPatternElement> Pattern { get; } = pattern;
	public IlProvenanceConstr Provenance { get; } = provenance;
	public IlFormatInfo FormatInfo { get; } = formatInfo;
}

/// <summary>
/// Formats patterns, pattern elements, and provenance constraints for failure messages.
/// </summary>
/// <remarks>
/// Nothing in this class should be called throughout a successful search. This is for failure formatting.
/// </remarks>
internal static class IlPatternDisplay {
	public static string FormatPattern(
		ReadOnlySpan<IlPatternElement> pattern,
		IlProvenanceConstr provenance,
		in IlFormatInfo info
	) {
		StringBuilder sb = new();
		for (int i = 0; i < pattern.Length; i++) {
			sb.Append(' ');
			sb.Append(i.ToString(CultureInfo.InvariantCulture).PadLeft(3));
			sb.Append(". ");
			sb.Append(FormatElement(pattern[i], in info));
			sb.AppendLine();
		}
		sb.Append("    + ");
		sb.Append(FormatProvenance(provenance));
		return sb.ToString();
	}

	public static string FormatPattern(in IlPatternSearchInfo info) =>
		FormatPattern(info.Pattern, info.Provenance, info.FormatInfo);

	public static string FormatElement(IlPatternElement element, in IlFormatInfo info) => element.Kind switch {
		IlPatternElement.PatternKind.Any => "<any instruction>",
		IlPatternElement.PatternKind.OpCode => formatAnyOperand(element.OpCodeValue),
		IlPatternElement.PatternKind.Instruction => IlInstructionDisplay.Format(element.OpCodeValue, element.Operand, null, info.FormatAnchor),
		_ => "<invalid pattern element>",
	};

	public static string FormatProvenance(IlProvenanceConstr provenance) => provenance.Kind switch {
		IlProvenanceConstr.ConstraintKind.Any => "any provenance",
		IlProvenanceConstr.ConstraintKind.AllFromOwner =>
			$"provenance owner '{provenance.OwnerId ?? throw new InternalStateException("AllFromOwner constraint has no owner ID")}'",
		IlProvenanceConstr.ConstraintKind.AllUnknown => "unknown provenance",
		IlProvenanceConstr.ConstraintKind.AllUniform => "uniform known provenance",
		_ => "<invalid provenance constraint>",
	};

	private static string formatAnyOperand(ILOpCode opCode) =>
		IlOpCodeInfo.GetOperandEncoding(opCode) == IlOperandEncoding.None
			? IlInstructionDisplay.FormatOpCode(opCode)
			: IlInstructionDisplay.FormatOpCode(opCode) + " <any operand>";
}
