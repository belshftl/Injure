// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Options for formatting IL for debugging.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and produces plain text with provenance local IDs
/// omitted and no extra leading newline added.
/// </remarks>
public readonly record struct IlFormatOptions {
	/// <summary>
	/// Whether the output should be colored with ANSI escape sequences.
	/// </summary>
	/// <remarks>
	/// Recommended if the output will be viewed in a terminal, as it makes it a lot more visually
	/// parsable; otherwise it's useless, since in e.g. a log file the escapes show up as raw text.
	/// </remarks>
	public bool Color { get; init; }

	/// <summary>
	/// Whether to include local IDs in provenance displays.
	/// </summary>
	/// <remarks>
	/// Doesn't affect the header, which always names the transaction in full (i.e. as if this option
	/// was on).
	/// </remarks>
	public bool IncludeProvenanceLocalIds { get; init; }

	/// <summary>
	/// Whether an additional leading newline should be added before any output.
	/// </summary>
	/// <remarks>
	/// The rationale is that if the output is logged immediately into a terminal, the header would
	/// otherwise go on the same line as whatever info the logger puts on that same line - for instance,
	/// with the default diagnostics sink, it'd get prefaced with something like
	/// <c>(12:25:37+0000) [MyMod@0001] [DEBUG] </c>.
	/// </remarks>
	public bool AddLeadingNewline { get; init; }
}

/// <summary>
/// Presets for <see cref="IlFormatOptions"/>, for ergonomics purposes.
/// </summary>
public static class IlFormatPreset {
	/// <summary>
	/// Preset for a standard "debug-log what I'm patching" workflow: color enabled, so the output in
	/// the terminal looks nicer and is more readable; provenance local IDs disabled, since they're
	/// usually not relevant when writing a patch, especially given matching only uses owner IDs; and
	/// a leading newline enabled (see <see cref="IlFormatOptions.AddLeadingNewline"/>'s docs).
	/// </summary>
	public static IlFormatOptions Debug { get; } = new() {
		Color = true,
		IncludeProvenanceLocalIds = false,
		AddLeadingNewline = true,
	};
}
