// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Injure.ModKit.Abstractions;

public static class ModMetadataValidation {
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
}
