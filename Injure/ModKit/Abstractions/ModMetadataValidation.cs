// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Injure.ModKit.Abstractions;

public static class ModMetadataValidation {
	/// <summary>
	/// Checks an owner ID for validity.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if <paramref name="s"/> is a valid owner ID;
	/// <see langword="false"/> otherwise.
	/// </returns>
	/// <param name="s">String to perform the check on.</param>
	/// <param name="err">If the method returned <see langword="false"/>, an error string describing the invalidity.</param>
	public static bool ValidateOwnerID(ReadOnlySpan<char> s, [NotNullWhen(false)] out string? err) {
		if (s.IsEmpty) {
			err = "owner ID must not be empty";
			return false;
		}
		if (!char.IsAsciiLetterOrDigit(s[0])) {
			err = "owner ID must start with an ASCII letter or ASCII digit";
			return false;
		}
		foreach (char c in s) {
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.')) {
				err = $"owner ID contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.')";
				return false;
			}
		}
		err = null;
		return true;
	}

	/// <summary>
	/// Checks an owner ID for validity, throwing <see cref="ArgumentException"/> if it's invalid.
	/// </summary>
	/// <inheritdoc cref="ValidateOwnerID(ReadOnlySpan{char}, out string?)"/>
	public static void ValidateOwnerIDOrThrow(ReadOnlySpan<char> s) {
		if (!ValidateOwnerID(s, out string? err))
			throw new ArgumentException(err);
	}
}
