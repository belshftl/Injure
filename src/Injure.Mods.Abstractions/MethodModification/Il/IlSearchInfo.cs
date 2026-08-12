// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection.Metadata;
using System.Text;

namespace Injure.Mods.Abstractions.MethodModification.Il;

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
			_ => throw new InternalStateException($"out of range IlSearchDirection enum value"),
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
	IlPatternProvenanceConstraint provenance,
	IlFormatContext context
) {
	public int StartInstructionBoundary { get; } = startInstrBoundary;
	public IlSearchDirection Direction { get; } = direction;
	public ReadOnlySpan<IlPatternElement> Pattern { get; } = pattern;
	public IlPatternProvenanceConstraint Provenance { get; } = provenance;
	public IlFormatContext Context { get; } = context;
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
		IlPatternProvenanceConstraint provenance,
		in IlFormatContext ctx
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

	public static string FormatElement(IlPatternElement element, in IlFormatContext ctx) => element.Kind switch {
		IlPatternElement.PatternKind.Any => "<any instruction>",
		IlPatternElement.PatternKind.OpCode => formatOpCode(element.OpCodeValue),
		IlPatternElement.PatternKind.Instruction => formatInstruction(element.OpCodeValue, element.Operand, in ctx),
		_ => "<invalid pattern element>",
	};

	public static string FormatProvenance(IlPatternProvenanceConstraint provenance) => provenance.Kind switch {
		IlPatternProvenanceConstraint.ConstraintKind.Any => "any provenance",
		IlPatternProvenanceConstraint.ConstraintKind.AllFromOwner =>
			$"provenance owner '{provenance.OwnerId ?? throw new InternalStateException("AllFromOwner constraint has no owner ID")}'",
		IlPatternProvenanceConstraint.ConstraintKind.AllUnknown => "unknown provenance",
		IlPatternProvenanceConstraint.ConstraintKind.AllUniform => "uniform known provenance",
		_ => "<invalid provenance constraint>",
	};

	private static string formatInstruction(ILOpCode opCode, IlOperand operand, in IlFormatContext ctx) {
		string opcodeDisplay = formatOpCode(opCode);
		return operand switch {
			IlNoneOperand => opcodeDisplay,
			IlInt32Operand o => $"{opcodeDisplay} {o.Value.ToString(CultureInfo.InvariantCulture)}",
			IlInt64Operand o => $"{opcodeDisplay} {o.Value.ToString(CultureInfo.InvariantCulture)}",
			IlFloat32Operand o => $"{opcodeDisplay} {o.Value.ToString(CultureInfo.InvariantCulture)}",
			IlFloat64Operand o => $"{opcodeDisplay} {o.Value.ToString(CultureInfo.InvariantCulture)}",
			IlStringOperand o => $"{opcodeDisplay} {formatString(o.Value)}",
			IlArgumentOperand o => $"{opcodeDisplay} {o.Index.ToString(CultureInfo.InvariantCulture)}",
			IlLocalOperand o => $"{opcodeDisplay} {o.Index.ToString(CultureInfo.InvariantCulture)}",
			IlTypeOperand o => $"{opcodeDisplay} {IlReferenceDisplay.FormatType(o.Type)}",
			IlMethodOperand o => $"{opcodeDisplay} {IlReferenceDisplay.FormatMethod(o.Method)}",
			IlFieldOperand o => $"{opcodeDisplay} {IlReferenceDisplay.FormatField(o.Field)}",
			IlCallSiteOperand o => $"{opcodeDisplay} {IlReferenceDisplay.FormatSignature(o.Signature)}",
			IlBranchOperand o => $"{opcodeDisplay} {ctx.FormatAnchor(o.Target)}",
			IlSwitchOperand o => formatSwitchOperand(opcodeDisplay, o, in ctx),
			_ => throw new InternalStateException($"unknown IL operand type '{operand.GetType()}'"),
		};
	}

	private static string formatSwitchOperand(string opcodeDisplay, IlSwitchOperand operand, in IlFormatContext ctx) {
		StringBuilder sb = new(opcodeDisplay);
		sb.Append(" (");
		for (int i = 0; i < operand.Targets.Length; i++) {
			if (i != 0)
				sb.Append(", ");
			sb.Append(ctx.FormatAnchor(operand.Targets[i]));
		}
		sb.Append(')');
		return sb.ToString();
	}

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

	private static string formatOpCode(ILOpCode opCode) =>
		opCode.ToString().Replace('_', '.').ToLowerInvariant();

	private static string formatString(string value) =>
		'"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal)
			.Replace("\r", "\\r", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal)
			.Replace("\t", "\\t", StringComparison.Ordinal)
			.Replace("\0", "\\0", StringComparison.Ordinal) + '"';
}
