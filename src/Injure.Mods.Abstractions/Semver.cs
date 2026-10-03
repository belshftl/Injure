// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Injure.Mods.Abstractions;

internal static partial class SemverRegex {
	public const string Pattern =
		@"^(?<maj>0|[1-9][0-9]*)\.(?<min>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$";

	[GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
	public static partial Regex Re();
}

/// <summary>
/// A version number as defined by Semantic Versioning 2.0.0. In other words, a semver.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is version <c>0.0.0</c>.
/// </remarks>
[JsonConverter(typeof(SemverJsonConverter))]
public readonly struct Semver : IEquatable<Semver>, ISpanParsable<Semver> {
	/// <summary>
	/// The major version.
	/// </summary>
	public int Major { get; }

	/// <summary>
	/// The minor version.
	/// </summary>
	public int Minor { get; }

	/// <summary>
	/// The patch version.
	/// </summary>
	public int Patch { get; }

	/// <summary>
	/// Dot-separated prerelease identifiers, without the leading <c>-</c>, or <see langword="null"/>
	/// if this is not a prerelease version.
	/// </summary>
	public string? Prerelease { get; }

	/// <summary>
	/// Dot-separated build metadata identifiers, without the leading <c>+</c>, or
	/// <see langword="null"/> if this version has no build metadata.
	/// </summary>
	public string? BuildMetadata { get; }

	/// <summary>
	/// Whether this version has prerelease identifiers, i.e. whether <see cref="Prerelease"/> is not
	/// <see langword="null"/>.
	/// </summary>
	public bool IsPrerelease => Prerelease is not null;

	/// <summary>
	/// Creates a <see cref="Semver"/> from its components.
	/// </summary>
	/// <param name="maj">The major version.</param>
	/// <param name="min">The minor version.</param>
	/// <param name="patch">The patch version.</param>
	/// <param name="pre">
	/// Dot-separated prerelease identifiers, without the leading <c>-</c>, or <see langword="null"/>
	/// for none.
	/// </param>
	/// <param name="build">
	/// Dot-separated build metadata identifiers, without the leading <c>+</c>, or
	/// <see langword="null"/> for none.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="maj"/>, <paramref name="min"/>, or <paramref name="patch"/> is
	/// negative.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="pre"/> or <paramref name="build"/> is not a valid sequence of
	/// identifiers.
	/// </exception>
	public Semver(int maj, int min, int patch, string? pre = null, string? build = null) {
		ArgumentOutOfRangeException.ThrowIfNegative(maj);
		ArgumentOutOfRangeException.ThrowIfNegative(min);
		ArgumentOutOfRangeException.ThrowIfNegative(patch);
		Major = maj;
		Minor = min;
		Patch = patch;
		Prerelease = validatePre(pre, nameof(pre));
		BuildMetadata = validateBuild(build, nameof(build));
	}

	/// <summary>
	/// Determines whether <paramref name="given"/> satisfies <paramref name="minimum"/> under
	/// Cargo-style caret/<c>^</c> semantics.
	/// </summary>
	/// <param name="minimum">The lowest acceptable version.</param>
	/// <param name="given">The version to test.</param>
	/// <remarks>
	/// <paramref name="given"/> is compatible iff all of the following hold:
	/// <list type="number">
	/// <item><description>
	/// <paramref name="given"/> has equal or higher precedence than <paramref name="minimum"/>.
	/// </description></item>
	/// <item><description>
	/// Up to and including the leftmost nonzero component of <paramref name="minimum"/> match
	/// <paramref name="given"/>'s exactly: for some <c>X &gt; 0</c>, <c>X.m.p</c> requires major to
	/// match exactly; <c>0.X.p</c> requires major and minor to match exactly; <c>0.0.X</c> or
	/// <c>0.0.0</c> require all three to match exactly.
	/// </description></item>
	/// <item><description>
	/// If <paramref name="given"/> is a prerelease, <paramref name="minimum"/> is also a prerelease
	/// with identical major/minor/patch.
	/// </description></item>
	/// </list>
	/// Build metadata is ignored.
	/// </remarks>
	public static bool Compatible(Semver minimum, Semver given) {
		if (
			given.Prerelease is not null
			&& (
				minimum.Prerelease is null
				|| given.Major != minimum.Major
				|| given.Minor != minimum.Minor
				|| given.Patch != minimum.Patch
			)
		)
			return false;
		if (given.Major != minimum.Major)
			return false;
		if (minimum.Major == 0) {
			if (given.Minor != minimum.Minor)
				return false;
			if (minimum.Minor == 0 && given.Patch != minimum.Patch)
				return false;
		}
		return cmpIgnoreBuild(given, minimum) >= 0;
	}

	/// <summary>
	/// Whether this version and <paramref name="other"/> are exactly equal, including build metadata.
	/// </summary>
	public bool Equals(Semver other) =>
		Major == other.Major && Minor == other.Minor && Patch == other.Patch
		&& string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal)
		&& string.Equals(BuildMetadata, other.BuildMetadata, StringComparison.Ordinal);
	/// <inheritdoc cref="Equals(Semver)"/>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is Semver other && Equals(other);
	/// <summary>
	/// Computes a hash consistent with <see cref="Equals(Semver)"/>.
	/// </summary>
	public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease, BuildMetadata);
	/// <summary>
	/// Equivalent to <see cref="Equals(Semver)"/>.
	/// </summary>
	public static bool operator ==(Semver left, Semver right) => left.Equals(right);
	/// <summary>
	/// Equivalent to the negation of <see cref="Equals(Semver)"/>.
	/// </summary>
	public static bool operator !=(Semver left, Semver right) => !left.Equals(right);

	/// <summary>
	/// Returns the canonical string form <c>MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]</c>.
	/// </summary>
	public override string ToString() {
		string s = $"{Major}.{Minor}.{Patch}";
		if (Prerelease is not null)
			s += "-" + Prerelease;
		if (BuildMetadata is not null)
			s += "+" + BuildMetadata;
		return s;
	}

	/// <summary>
	/// Attempts to parse a full Semantic Versioning 2.0.0 version string.
	/// </summary>
	/// <param name="s">The character span to parse.</param>
	/// <param name="val">The parsed semver on success; otherwise, <see langword="default"/>.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="s"/> was successfully parsed; otherwise,
	/// <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// The input must be an exact match: no surrounding whitespace, no <c>v</c> prefix, and all three
	/// core components present. Each core component must fit in an <see cref="int"/>.
	/// </remarks>
	public static bool TryParse(ReadOnlySpan<char> s, out Semver val) {
		val = default;
		if (!SemverRegex.Re().IsMatch(s))
			return false;
		int coreEnd = s.IndexOfAny('-', '+');
		if (coreEnd < 0)
			coreEnd = s.Length;
		ReadOnlySpan<char> core = s[..coreEnd];
		int dot1 = core.IndexOf('.');
		int dot2 = dot1 + 1 + core[(dot1 + 1)..].IndexOf('.');
		if (
			!int.TryParse(core[..dot1], NumberStyles.None, CultureInfo.InvariantCulture, out int maj)
			|| !int.TryParse(core[(dot1 + 1)..dot2], NumberStyles.None, CultureInfo.InvariantCulture, out int min)
			|| !int.TryParse(core[(dot2 + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int patch)
		)
			return false;
		ReadOnlySpan<char> rest = s[coreEnd..];
		string? pre = null;
		string? build = null;
		int plus = rest.IndexOf('+');
		if (plus >= 0) {
			build = rest[(plus + 1)..].ToString();
			rest = rest[..plus];
		}
		if (!rest.IsEmpty)
			pre = rest[1..].ToString();
		if (pre is not null && !isValidDottedIdents(pre, prerelease: true))
			return false;
		if (build is not null && !isValidDottedIdents(build, prerelease: false))
			return false;
		val = new Semver(maj, min, patch, pre, build);
		return true;
	}

	/// <inheritdoc cref="TryParse(ReadOnlySpan{char}, out Semver)"/>
	/// <param name="s">The string to parse.</param>
	/// <param name="val">The parsed semver on success; otherwise, <see langword="default"/>.</param>
	public static bool TryParse([NotNullWhen(true)] string? s, out Semver val) {
		if (s is null) {
			val = default;
			return false;
		}
		return TryParse(s.AsSpan(), out val);
	}
	/// <inheritdoc cref="TryParse(ReadOnlySpan{char}, out Semver)"/>
	/// <param name="s">The character span to parse.</param>
	/// <param name="provider">Ignored.</param>
	/// <param name="val">The parsed semver on success; otherwise, <see langword="default"/>.</param>
	public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Semver val) =>
		TryParse(s, out val);
	/// <inheritdoc cref="TryParse(string, out Semver)"/>
	/// <param name="s">The string to parse.</param>
	/// <param name="provider">Ignored.</param>
	/// <param name="val">The parsed semver on success; otherwise, <see langword="default"/>.</param>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Semver val) =>
		TryParse(s, out val);

	/// <summary>
	/// Parses a full Semantic Versioning 2.0.0 version string.
	/// </summary>
	/// <param name="s">The character span to parse.</param>
	/// <returns>The parsed semver value.</returns>
	/// <exception cref="FormatException">
	/// Thrown if <paramref name="s"/> is not a valid semver.
	/// </exception>
	/// <remarks>
	/// The input must be an exact match: no surrounding whitespace, no <c>v</c> prefix, and all three
	/// core components present. Each core component must fit in an <see cref="int"/>.
	/// </remarks>
	public static Semver Parse(ReadOnlySpan<char> s) {
		if (TryParse(s, out Semver val))
			return val;
		throw new FormatException(
			$"string is not a valid full semver: must be of the form MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD] and core numeric components must each fit in an int32"
		);
	}

	/// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
	/// <param name="s">The string to parse.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="s"/> is <see langword="null"/>.
	/// </exception>
	public static Semver Parse([NotNull] string? s) {
		ArgumentNullException.ThrowIfNull(s);
		return Parse(s.AsSpan());
	}
	/// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
	/// <param name="s">The character span to parse.</param>
	/// <param name="provider">Ignored.</param>
	public static Semver Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
	/// <inheritdoc cref="Parse(string)"/>
	/// <param name="s">The string to parse.</param>
	/// <param name="provider">Ignored.</param>
	public static Semver Parse([NotNull] string? s, IFormatProvider? provider) => Parse(s);

	private static string? validatePre(string? value, string paramName) {
		if (value is null)
			return null;
		if (!isValidDottedIdents(value, prerelease: true))
			throw new ArgumentException("invalid semver prerelease identifiers", paramName);
		return value;
	}
	private static string? validateBuild(string? value, string paramName) {
		if (value is null)
			return null;
		if (!isValidDottedIdents(value, prerelease: false))
			throw new ArgumentException("invalid semver build metadata identifiers", paramName);
		return value;
	}
	private static bool isValidDottedIdents(ReadOnlySpan<char> s, bool prerelease) {
		if (s.IsEmpty)
			return false;
		int start = 0;
		for (;;) {
			int dot = s[start..].IndexOf('.');
			if (dot < 0)
				return isValidIdent(s[start..], prerelease);
			if (!isValidIdent(s.Slice(start, dot), prerelease))
				return false;
			start += dot + 1;
			if (start >= s.Length)
				return false;
		}
	}
	private static bool isValidIdent(ReadOnlySpan<char> id, bool prerelease) {
		if (id.IsEmpty)
			return false;
		bool allDigits = true;
		foreach (char c in id) {
			bool digit = char.IsAsciiDigit(c);
			if (!digit)
				allDigits = false;
			if (!digit && !char.IsAsciiLetter(c) && c != '-')
				return false;
		}
		return !prerelease || !allDigits || id.Length == 1 || id[0] != '0';
	}

	private static int cmpIgnoreBuild(Semver left, Semver right) {
		int cmp = left.Major.CompareTo(right.Major);
		if (cmp != 0)
			return cmp;
		cmp = left.Minor.CompareTo(right.Minor);
		if (cmp != 0)
			return cmp;
		cmp = left.Patch.CompareTo(right.Patch);
		if (cmp != 0)
			return cmp;
		if (left.Prerelease is null && right.Prerelease is null)
			return 0;
		if (left.Prerelease is null)
			return 1;
		if (right.Prerelease is null)
			return -1;
		return cmpPrerelease(left.Prerelease, right.Prerelease);
	}

	private static int cmpPrerelease(string left, string right) {
		int li = 0;
		int ri = 0;
		while (li < left.Length && ri < right.Length) {
			ReadOnlySpan<char> lid = nextIdent(left, ref li);
			ReadOnlySpan<char> rid = nextIdent(right, ref ri);

			bool lnum = isNumeric(lid);
			bool rnum = isNumeric(rid);

			int cmp;
			if (lnum && rnum)
				cmp = cmpNumericIdent(lid, rid);
			else if (lnum != rnum)
				cmp = lnum ? -1 : 1;
			else
				cmp = cmpAscii(lid, rid);

			if (cmp != 0)
				return cmp;
		}
		if (li == left.Length)
			return ri == right.Length ? 0 : -1;
		return 1;
	}

	private static ReadOnlySpan<char> nextIdent(ReadOnlySpan<char> s, ref int index) {
		int start = index;
		int dot = s[start..].IndexOf('.');
		if (dot < 0) {
			index = s.Length;
			return s[start..];
		}
		index = start + dot + 1;
		return s.Slice(start, dot);
	}

	private static int cmpNumericIdent(ReadOnlySpan<char> left, ReadOnlySpan<char> right) {
		if (left.Length != right.Length)
			return left.Length.CompareTo(right.Length);
		return cmpAscii(left, right);
	}

	private static int cmpAscii(ReadOnlySpan<char> left, ReadOnlySpan<char> right) {
		int len = Math.Min(left.Length, right.Length);
		for (int i = 0; i < len; i++) {
			int cmp = left[i].CompareTo(right[i]);
			if (cmp != 0)
				return cmp;
		}
		return left.Length.CompareTo(right.Length);
	}

	private static bool isNumeric(ReadOnlySpan<char> s) {
		foreach (char c in s)
			if (!char.IsAsciiDigit(c))
				return false;
		return !s.IsEmpty;
	}
}

/// <summary>
/// Compares <see cref="Semver"/> values for equality with build metadata ignored, i.e. equal
/// precedence under Semantic Versioning 2.0.0.
/// </summary>
public sealed class SemverIgnoreBuildEqualityComparer : IEqualityComparer<Semver> {
	/// <summary>
	/// The singleton instance.
	/// </summary>
	public static SemverIgnoreBuildEqualityComparer Instance { get; } = new();

	private SemverIgnoreBuildEqualityComparer() {
	}

	/// <summary>
	/// Determines whether <paramref name="x"/> and <paramref name="y"/> have identical major, minor,
	/// patch, and prerelease components.
	/// </summary>
	public bool Equals(Semver x, Semver y) =>
		x.Major == y.Major && x.Minor == y.Minor && x.Patch == y.Patch
		&& string.Equals(x.Prerelease, y.Prerelease, StringComparison.Ordinal);

	/// <summary>
	/// Computes a hash consistent with <see cref="Equals(Semver, Semver)"/>, i.e. one that ignores
	/// build metadata.
	/// </summary>
	public int GetHashCode(Semver obj) => HashCode.Combine(obj.Major, obj.Minor, obj.Patch, obj.Prerelease);
}

/// <summary>
/// Converts <see cref="Semver"/> to and from a JSON string in the form produced by
/// <see cref="Semver.ToString"/>.
/// </summary>
public sealed class SemverJsonConverter : JsonConverter<Semver> {
	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException">
	/// Thrown if the token is a JSON <c>null</c>.
	/// </exception>
	/// <exception cref="FormatException">
	/// Thrown if the string is not a valid version.
	/// </exception>
	public override Semver Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		Semver.Parse(reader.GetString());

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, Semver val, JsonSerializerOptions options) =>
		writer.WriteStringValue(val.ToString());
}
