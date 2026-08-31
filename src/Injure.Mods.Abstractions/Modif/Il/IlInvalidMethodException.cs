// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

internal sealed class IlInvalidMethodException(string msg, int instrBoundary = -1, IlInstructionId instrId = default) : Exception(fmt(msg, instrBoundary)) {
	public int InstructionBoundary { get; } = instrBoundary;
	public IlInstructionId InstructionId { get; } = instrId;
	private static string fmt(string msg, int boundary) =>
		boundary < 0 ? msg : $"{msg} (at instruction boundary {boundary.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
}
