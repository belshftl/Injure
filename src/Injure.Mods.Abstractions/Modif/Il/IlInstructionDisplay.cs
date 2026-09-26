// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection.Metadata;
using System.Text;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Formats single instructions for human-readable output.
/// </summary>
/// <remarks>
/// Used by both match failure messages and body debug-formats. They differ in how branch targets
/// are named, so that part is left to the caller.
/// </remarks>
internal static class IlInstructionDisplay {
	/// <summary>
	/// Formats an instruction whose branch/switch operand, if any, is already resolved to anchors.
	/// </summary>
	public static string Format(ILOpCode opCode, IlOperand operand, IlInstructionPrefixes? prefixes, Func<IlAnchorId, string> formatTarget) {
		string instr = operand switch {
			IlBranchOperand o => FormatWithTargets(opCode, [formatTarget(o.Target)]),
			IlSwitchOperand o => FormatWithTargets(opCode, o.Targets.Select(formatTarget).ToArray()),
			_ => formatNonBranch(opCode, operand),
		};
		return prefixes is null ? instr : FormatPrefixes(prefixes) + " " + instr;
	}

	/// <summary>
	/// Formats a branch or switch whose targets the caller has already named.
	/// </summary>
	/// <remarks>
	/// For instructions that don't have an anchor-based operand yet, like a pending branch to a label.
	/// </remarks>
	public static string FormatWithTargets(ILOpCode opCode, IReadOnlyList<string> targets) =>
		opCode == ILOpCode.Switch
			? $"{FormatOpCode(opCode)} ({string.Join(", ", targets)})"
			: $"{FormatOpCode(opCode)} {targets.Single()}";

	public static string FormatOpCode(ILOpCode opCode) =>
		opCode.ToString().Replace('_', '.').ToLowerInvariant();

	public static string FormatPrefixes(IlInstructionPrefixes prefixes) {
		StringBuilder sb = new();
		if (prefixes.Has(IlPrefixFlags.Constrained))
			sb.Append("constrained. ").Append(IlRefDisplay.FormatType(prefixes.ConstrainedType!)).Append(' ');
		if (prefixes.Has(IlPrefixFlags.No))
			sb.Append("no. ").Append(formatSkipChecks(prefixes.SkipChecks)).Append(' ');
		if (prefixes.Has(IlPrefixFlags.ReadOnly))
			sb.Append("readonly. ");
		if (prefixes.Has(IlPrefixFlags.Tail))
			sb.Append("tail. ");
		if (prefixes.Has(IlPrefixFlags.Unaligned))
			sb.Append("unaligned. ").Append(prefixes.Alignment.ToString(CultureInfo.InvariantCulture)).Append(' ');
		if (prefixes.Has(IlPrefixFlags.Volatile))
			sb.Append("volatile. ");
		return sb.ToString(0, sb.Length - 1);
	}

	public static string FormatString(string value) =>
		'"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal)
			.Replace("\r", "\\r", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal)
			.Replace("\t", "\\t", StringComparison.Ordinal)
			.Replace("\0", "\\0", StringComparison.Ordinal) + '"';

	private static string formatNonBranch(ILOpCode opCode, IlOperand operand) {
		string op = FormatOpCode(opCode);
		return operand switch {
			IlNoneOperand => op,
			IlInt32Operand o => $"{op} {o.Value.ToString(CultureInfo.InvariantCulture)}",
			IlInt64Operand o => $"{op} {o.Value.ToString(CultureInfo.InvariantCulture)}",
			IlFloat32Operand o => $"{op} {o.Value.ToString(CultureInfo.InvariantCulture)}",
			IlFloat64Operand o => $"{op} {o.Value.ToString(CultureInfo.InvariantCulture)}",
			IlStringOperand o => $"{op} {FormatString(o.Value)}",
			IlArgumentOperand o => $"{op} {o.Index.ToString(CultureInfo.InvariantCulture)}",
			IlLocalOperand o => $"{op} {o.Index.ToString(CultureInfo.InvariantCulture)}",
			IlTypeOperand o => $"{op} {IlRefDisplay.FormatType(o.Type)}",
			IlMethodOperand o => $"{op} {IlRefDisplay.FormatMethod(o.Method)}",
			IlFieldOperand o => $"{op} {IlRefDisplay.FormatField(o.Field)}",
			IlCallSiteOperand o => $"{op} {IlRefDisplay.FormatSignature(o.Signature)}",
			_ => throw InternalStateException.BadClosedHierarchy(operand),
		};
	}

	private static string formatSkipChecks(IlSkipChecks checks) {
		List<string> names = new(3);
		if ((checks & IlSkipChecks.TypeCheck) != 0)
			names.Add("typecheck");
		if ((checks & IlSkipChecks.RangeCheck) != 0)
			names.Add("rangecheck");
		if ((checks & IlSkipChecks.NullCheck) != 0)
			names.Add("nullcheck");
		return string.Join(", ", names);
	}
}
