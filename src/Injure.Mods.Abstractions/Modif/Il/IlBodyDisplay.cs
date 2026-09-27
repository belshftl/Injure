// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Formats a method body as a human-readable listing for debug purposes. The output is not meant
/// to be machine-parsed, and changes to the format will not be treated as an API break.
/// </summary>
internal static class IlBodyDisplay {
	/// <summary>
	/// Instruction text wider than this doesn't count for the left-padding calculation for the
	/// provenance comment.
	/// </summary>
	private const int maxAlignedWidth = 80;

	/// <summary>
	/// Minimum no. digits reserved for row numbers.
	/// </summary>
	private const int minNumberDigits = 4;

	private const string unknownProvenance = "<unknown provenance>";

	private const string bold = "1";
	private const string faint = "2";
	private const string green = "32";

	// red and yellow are also left out because they're reserved for uncommitted removals/mutations
	// when removal/mutation edits become a thing
	private static readonly string[] ownerPalette = ["34", "35", "36", "94", "95", "96"];

	private readonly record struct Segment(string Text, string? Style);
	private readonly record struct Block(int Start, int End, string Header);

	public static string Format(IlMethodBody body, in IlFormatOptions options) =>
		Format(IlBodyView.FromBody(body), in options);

	public static string Format(IlBodyView view, in IlFormatOptions options) {
		InternalStateException.ThrowIfNull(view);

		Dictionary<IlAnchorId, int> boundaryOf = new(view.AnchorAt.Count);
		for (int boundary = 0; boundary < view.AnchorAt.Count; boundary++)
			if (view.AnchorAt[boundary] is IlAnchorId anchor)
				boundaryOf.Add(anchor, boundary);

		Dictionary<int, string> labelNames = nameTargets(view, boundaryOf);
		List<Block> blocks = collectBlocks(view, boundaryOf);

		int maxNumber = Math.Max(
			view.Rows.Count == 0 ? 0 : view.Rows.Max(static r => r.Index),
			view.Locals.Length + view.DeclaredLocals.Length - 1
		);
		int numberWidth = Math.Max(minNumberDigits, digits(maxNumber)) + 1;

		string[] texts = new string[view.Rows.Count];
		for (int i = 0; i < view.Rows.Count; i++)
			texts[i] = formatRow(view, view.Rows[i], boundaryOf, labelNames);
		int textWidth = alignedWidth(texts);

		StringBuilder sb = new();
		if (options.AddLeadingNewline)
			sb.AppendLine();
		sb.Append("IL for '")
			.Append(IlRefDisplay.FormatType(view.Method.Signature.ReturnType))
			.Append(' ')
			.Append(IlRefDisplay.FormatMethod(view.Method))
			.Append('\'');
		if (view.TransactionName is not null)
			sb.Append(", in transaction '").Append(view.TransactionName).Append('\'');
		sb.AppendLine();

		if (view.Locals.IsEmpty && view.DeclaredLocals.IsEmpty) {
			sb.AppendLine("locals: none");
		} else {
			string[] existing = view.Locals.Select(IlRefDisplay.FormatType).ToArray();
			string[] declared = view.DeclaredLocals.Select(IlRefDisplay.FormatType).ToArray();
			int typeWidth = alignedWidth([.. existing, .. declared]);
			string declaredProvenance = formatProvenance(view.DeclaredLocalsProvenance, options);

			sb.AppendLine(view.InitLocals ? "locals (zeroed):" : "locals (uninit):");
			for (int i = 0; i < existing.Length; i++) {
				string? style = ownerStyle(view.LocalsProvenance[i], i < view.BaselineLocalCount);
				List<Segment> line = gutter(' ', i, numberWidth, style);
				appendCommented(line, existing[i], style, typeWidth, formatProvenance(view.LocalsProvenance[i], options));
				appendLine(sb, line, in options);
			}
			for (int i = 0; i < declared.Length; i++) {
				List<Segment> line = gutter('+', existing.Length + i, numberWidth, green);
				appendCommented(line, declared[i], green, typeWidth, declaredProvenance);
				appendLine(sb, line, in options);
			}
		}

		sb.AppendLine();
		sb.AppendLine("code:");
		int depth = 0;
		for (int boundary = 0; boundary <= view.Rows.Count; boundary++) {
			depth = appendRegionEdges(sb, blocks, boundary, depth);
			if (labelNames.TryGetValue(boundary, out string? label))
				sb.Append(label).AppendLine(":");
			if (boundary == view.Rows.Count)
				break;

			IlBodyRow row = view.Rows[boundary];
			string? style = rowStyle(row);
			List<Segment> line = gutter(row.Status == IlRowStatus.Inserted ? '+' : ' ', row.Index, numberWidth, style);
			appendCommented(line, texts[boundary], style, textWidth, formatProvenance(row.Provenance, options));
			appendLine(sb, line, in options);
		}
		return sb.ToString();
	}

	private static string formatProvenance(InternalIlProvenance provenance, IlFormatOptions options) =>
		provenance.IsUnknown ? unknownProvenance :
		options.IncludeProvenanceLocalIds ? provenance.ToString() : provenance.GetOwnerId()!;

	// ==========================================================================================
	// rows
	private static string formatRow(
		IlBodyView view,
		IlBodyRow row,
		Dictionary<IlAnchorId, int> boundaryOf,
		Dictionary<int, string> labelNames
	) {
		if (row.Labels is not null) {
			string[] targets = new string[row.Labels.Length];
			for (int i = 0; i < targets.Length; i++)
				targets[i] = view.LabelBoundaries.TryGetValue(row.Labels[i].LabelId, out int boundary)
					? labelNames[boundary]
					: "<unmarked label>";
			return IlInstructionDisplay.FormatWithTargets(row.OpCode, targets);
		}
		return IlInstructionDisplay.Format(row.OpCode, row.Operand, row.Prefixes, anchor => labelNames[boundaryOf[anchor]]);
	}

	/// <summary>
	/// Names every branch target in listing order.
	/// </summary>
	private static Dictionary<int, string> nameTargets(IlBodyView view, Dictionary<IlAnchorId, int> boundaryOf) {
		SortedSet<int> targets = new();
		foreach (IlBodyRow row in view.Rows) {
			switch (row.Operand) {
			case IlBranchOperand branch:
				targets.Add(boundaryOf[branch.Target]);
				break;
			case IlSwitchOperand @switch:
				foreach (IlAnchorId target in @switch.Targets)
					targets.Add(boundaryOf[target]);
				break;
			}
			if (row.Labels is not null)
				foreach (IlLabel label in row.Labels)
					if (view.LabelBoundaries.TryGetValue(label.LabelId, out int boundary))
						targets.Add(boundary);
		}

		Dictionary<int, string> names = new(targets.Count);
		foreach (int boundary in targets)
			names.Add(boundary, "L" + names.Count.ToString(CultureInfo.InvariantCulture));
		return names;
	}

	private static string? rowStyle(IlBodyRow row) =>
		row.Status == IlRowStatus.Inserted ? green : ownerStyle(row.Provenance, row.IsBaseline);

	private static string? ownerStyle(InternalIlProvenance provenance, bool isBaseline) =>
		(isBaseline || provenance.IsUnknown)
			? null
			: ownerPalette[fnv(provenance.GetOwnerId()!) % ownerPalette.Length];

	// ==========================================================================================
	// exception regions
	private static List<Block> collectBlocks(IlBodyView view, Dictionary<IlAnchorId, int> boundaryOf) {
		List<Block> blocks = new();
		HashSet<(int, int)> tries = new();
		foreach (IlExceptionRegion region in view.ExceptionRegions) {
			int tryStart = boundaryOf[region.TryStart];
			int tryEnd = boundaryOf[region.TryEnd];
			int handlerStart = boundaryOf[region.HandlerStart];
			int handlerEnd = boundaryOf[region.HandlerEnd];

			if (tries.Add((tryStart, tryEnd)))
				blocks.Add(new Block(tryStart, tryEnd, ".try"));

			switch (region.Kind) {
			case IlExceptionRegionKind.Catch:
				blocks.Add(new Block(handlerStart, handlerEnd, "catch " + IlRefDisplay.FormatType(region.CatchType!)));
				break;
			case IlExceptionRegionKind.Filter:
				blocks.Add(new Block(boundaryOf[region.FilterStart!.Value], handlerStart, "filter"));
				blocks.Add(new Block(handlerStart, handlerEnd, "filtered catch"));
				break;
			case IlExceptionRegionKind.Finally:
				blocks.Add(new Block(handlerStart, handlerEnd, "finally"));
				break;
			case IlExceptionRegionKind.Fault:
				blocks.Add(new Block(handlerStart, handlerEnd, "fault"));
				break;
			default:
				throw new InternalStateException($"unknown exception region kind '{region.Kind}'");
			}
		}
		return blocks;
	}

	private static int appendRegionEdges(StringBuilder sb, List<Block> blocks, int boundary, int depth) {
		var closes = blocks.Where(b => b.End == boundary).OrderByDescending(static b => b.Start).ToList();
		var opens = blocks.Where(b => b.Start == boundary).OrderByDescending(static b => b.End).ToList();

		int opened = 0;
		for (int i = 0; i < closes.Count; i++) {
			depth--;
			bool last = i == closes.Count - 1;
			if (last && opens.Count > 0) {
				sb.Append(' ', 2 * depth).Append("} ").Append(opens[0].Header).AppendLine(" {");
				depth++;
				opened = 1;
			} else {
				sb.Append(' ', 2 * depth).AppendLine("}");
			}
		}
		for (int i = opened; i < opens.Count; i++) {
			sb.Append(' ', 2 * depth).Append(opens[i].Header).AppendLine(" {");
			depth++;
		}
		return depth;
	}

	// ==========================================================================================
	// layout
	private static List<Segment> gutter(char marker, int number, int numberWidth, string? style) {
		string text = number < 0
			? marker + new string(' ', numberWidth + 2)
			: marker + number.ToString(CultureInfo.InvariantCulture).PadLeft(numberWidth) + ". ";
		if (number < 0)
			return [new Segment(text, style)];
		int numberStart = 1 + numberWidth - digits(number);
		return [
			new Segment(text[..numberStart], style),
			new Segment(text[numberStart..(1 + numberWidth)], style is null ? bold : bold + ";" + style),
			new Segment(text[(1 + numberWidth)..], style),
		];
	}

	private static int alignedWidth(IEnumerable<string> texts) {
		int width = 0;
		foreach (string text in texts)
			if (text.Length <= maxAlignedWidth)
				width = Math.Max(width, text.Length);
		return width;
	}

	private static void appendCommented(List<Segment> line, string text, string? style, int width, string comment) {
		line.Add(new Segment(text, style));
		line.Add(new Segment(new string(' ', Math.Max(1, width - text.Length + 1)), null));
		line.Add(new Segment("// " + comment, faint));
	}

	private static void appendLine(StringBuilder sb, List<Segment> line, in IlFormatOptions options) {
		string? current = null;
		foreach (Segment segment in line) {
			if (segment.Text.Length == 0)
				continue;
			if (options.Color && segment.Style != current && !string.IsNullOrWhiteSpace(segment.Text)) {
				sb.Append(segment.Style is null ? "\x1b[0m" : $"\x1b[0;{segment.Style}m");
				current = segment.Style;
			}
			sb.Append(segment.Text);
		}
		if (current is not null)
			sb.Append("\x1b[0m");
		sb.AppendLine();
	}

	private static int digits(int value) => value < 10 ? 1 : value.ToString(CultureInfo.InvariantCulture).Length;

	private static uint fnv(string s) {
		uint h = 2166136261u;
		for (int i = 0; i < s.Length; i++) {
			h ^= s[i];
			h *= 16777619u;
		}
		return h;
	}
}
