// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection.Metadata;
using System.Text;

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
	IlFormatCtx ctx
) {
	public int StartInstructionBoundary { get; } = startInstrBoundary;
	public IlSearchDirection Direction { get; } = direction;
	public ReadOnlySpan<IlPatternElement> Pattern { get; } = pattern;
	public IlProvenanceConstr Provenance { get; } = provenance;
	public IlFormatCtx Context { get; } = ctx;
}

/// <summary>
/// Formats patterns, pattern elements, and provenance constraints for failure messages.
/// </summary>
/// <remarks>
/// Nothing in this class should be called throughout a successful search. This is for failure formatting.
/// </remarks>
internal static class IlPatternDisplay {
	private const string shortFormSuffix = " // or short-form equivalent";

	public static string FormatPattern(
		ReadOnlySpan<IlPatternElement> pattern,
		IlProvenanceConstr provenance,
		in IlFormatCtx ctx
	) {
		StringBuilder sb = new();
		for (int i = 0; i < pattern.Length; i++) {
			sb.Append(' ');
			sb.Append(i.ToString(CultureInfo.InvariantCulture).PadLeft(3));
			sb.Append(". ");
			sb.Append(FormatElement(pattern[i], in ctx));
			if (matchesEquivalentShorterForms(pattern[i]))
				sb.Append(shortFormSuffix);
			sb.AppendLine();
		}
		sb.Append("    + ");
		sb.Append(FormatProvenance(provenance));
		return sb.ToString();
	}

	public static string FormatPattern(in IlPatternSearchInfo info) =>
		FormatPattern(info.Pattern, info.Provenance, info.Context);

	public static string FormatElement(IlPatternElement element, in IlFormatCtx ctx) => element.Kind switch {
		IlPatternElement.PatternKind.Any => "<any instruction>",
		IlPatternElement.PatternKind.OpCode => formatOpCode(element.OpCodeValue),
		IlPatternElement.PatternKind.Instruction => formatInstruction(element.OpCodeValue, element.Operand, in ctx),
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

	private static string formatInstruction(ILOpCode opCode, IlOperand operand, in IlFormatCtx ctx) =>
		IlInstructionDisplay.Format(opCode, operand, null, ctx.FormatAnchor);

	private static bool matchesEquivalentShorterForms(IlPatternElement element) {
		if (element.Kind == IlPatternElement.PatternKind.Any)
			return false;
		return element.OpCodeValue is
			ILOpCode.Ldc_i4 or
			ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or
			ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc or
			ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or
			ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt or
			ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un or ILOpCode.Ble_un or ILOpCode.Blt_un or
			ILOpCode.Leave;
	}

	private static string formatOpCode(ILOpCode opCode) => IlInstructionDisplay.FormatOpCode(opCode);
}
