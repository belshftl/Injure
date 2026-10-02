// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Argument checks for the public APIs that take raw opcodes.
/// </summary>
internal static class IlOpCodeArguments {
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="opCode"/> isn't a defined opcode, is a prefix, or is a compact encoding.
	/// </exception>
	public static void ThrowIfNotCanonical(ILOpCode opCode, string paramName) {
		IlOpCodeDescriptor d = IlOpCodeInfo.GetDescriptor(opCode);
		if (!d.IsDefined)
			throw new ArgumentException($"0x{(int)opCode:x4} is not a defined opcode", paramName);
		if (d.Prefix != IlPrefixKind.None)
			throw new ArgumentException($"0x{(int)opCode:x4} is a prefix, not an instruction", paramName);
		if (d.Canonical != opCode)
			throw new ArgumentException(
				$"{IlInstructionDisplay.FormatOpCode(opCode)} is a compact encoding; use {IlInstructionDisplay.FormatOpCode(d.Canonical)}",
				paramName
			);
	}
}
