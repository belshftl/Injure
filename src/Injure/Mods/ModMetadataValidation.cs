// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods;

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
	public static bool ValidateOwnerId(ReadOnlySpan<char> s, [NotNullWhen(false)] out string? err) {
		if (s.IsEmpty) {
			err = "owner ID must not be empty";
			return false;
		}
		if (s.Length > 64) {
			err = "owner ID must be at most 64 characters long";
			return false;
		}
		if (!char.IsAsciiLetterOrDigit(s[0])) {
			err = "owner ID must start with an ASCII letter or ASCII digit";
			return false;
		}
		foreach (char c in s)
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.')) {
				err = $"owner ID contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.')";
				return false;
			}
		err = null;
		return true;
	}

	/// <summary>
	/// Checks an owner ID for validity.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if <paramref name="s"/> is a valid owner ID;
	/// <see langword="false"/> otherwise.
	/// </returns>
	/// <param name="s">String to perform the check on.</param>
	/// <param name="err">If the method returned <see langword="false"/>, an error string describing the invalidity.</param>
	public static bool ValidateOwnerId([NotNullWhen(true)] string? s, [NotNullWhen(false)] out string? err) {
		if (s is null) {
			err = "owner ID must not be null";
			return false;
		}
		return ValidateOwnerId(s.AsSpan(), out err);
	}

	/// <summary>
	/// Checks an owner ID for validity, throwing <see cref="ArgumentException"/> if it's invalid.
	/// </summary>
	/// <inheritdoc cref="ValidateOwnerId(ReadOnlySpan{char}, out string?)"/>
	public static void ValidateOwnerIdOrThrow(ReadOnlySpan<char> s) {
		if (!ValidateOwnerId(s, out string? err))
			throw new ArgumentException(err);
	}

	/// <summary>
	/// Checks an owner ID for validity, throwing <see cref="ArgumentException"/> if it's invalid.
	/// </summary>
	/// <inheritdoc cref="ValidateOwnerId(ReadOnlySpan{char}, out string?)"/>
	public static void ValidateOwnerIdOrThrow([NotNull] string? s) {
		ArgumentNullException.ThrowIfNull(s);
		if (!ValidateOwnerId(s.AsSpan(), out string? err))
			throw new ArgumentException(err);
	}

	/// <summary>
	/// Checks a local ID for validity.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if <paramref name="s"/> is a valid local ID;
	/// <see langword="false"/> otherwise.
	/// </returns>
	/// <param name="s">String to perform the check on.</param>
	/// <param name="err">If the method returned <see langword="false"/>, an error string describing the invalidity.</param>
	public static bool ValidateLocalId(ReadOnlySpan<char> s, [NotNullWhen(false)] out string? err) {
		if (s.IsEmpty) {
			err = "local ID must not be empty";
			return false;
		}
		if (s.Length > 256) {
			err = "local ID must be at most 256 characters long";
			return false;
		}
		if (!char.IsAsciiLetterOrDigit(s[0])) {
			err = "local ID must start with an ASCII letter or ASCII digit";
			return false;
		}
		foreach (char c in s)
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '/' || c == '@' || c == '#' || c == '+')) {
				err = $"local ID contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.', '/', '@', '#', '+')";
				return false;
			}
		err = null;
		return true;
	}

	/// <summary>
	/// Checks a local ID for validity.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if <paramref name="s"/> is a valid local ID;
	/// <see langword="false"/> otherwise.
	/// </returns>
	/// <param name="s">String to perform the check on.</param>
	/// <param name="err">If the method returned <see langword="false"/>, an error string describing the invalidity.</param>
	public static bool ValidateLocalId([NotNullWhen(true)] string? s, [NotNullWhen(false)] out string? err) {
		if (s is null) {
			err = "local ID must not be null";
			return false;
		}
		return ValidateLocalId(s.AsSpan(), out err);
	}

	/// <summary>
	/// Checks a local ID for validity, throwing <see cref="ArgumentException"/> if it's invalid.
	/// </summary>
	/// <inheritdoc cref="ValidateLocalId(ReadOnlySpan{char}, out string?)"/>
	public static void ValidateLocalIdOrThrow(ReadOnlySpan<char> s) {
		if (!ValidateLocalId(s, out string? err))
			throw new ArgumentException(err);
	}

	/// <summary>
	/// Checks a local ID for validity, throwing <see cref="ArgumentException"/> if it's invalid.
	/// </summary>
	/// <inheritdoc cref="ValidateLocalId(ReadOnlySpan{char}, out string?)"/>
	public static void ValidateLocalIdOrThrow([NotNull] string? s) {
		ArgumentNullException.ThrowIfNull(s);
		if (!ValidateLocalId(s, out string? err))
			throw new ArgumentException(err);
	}
}
