// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace Injure.Mods.Abstractions.Tests;

public static class SemverTests {
	[Theory]
	[InlineData("0.0.0", 0, 0, 0, null, null)]
	[InlineData("1.2.3", 1, 2, 3, null, null)]
	[InlineData("10.20.30", 10, 20, 30, null, null)]
	[InlineData("1.2.3-alpha", 1, 2, 3, "alpha", null)]
	[InlineData("1.2.3-alpha.1", 1, 2, 3, "alpha.1", null)]
	[InlineData("1.2.3-0.a.0", 1, 2, 3, "0.a.0", null)]
	[InlineData("1.2.3-a-b.c--", 1, 2, 3, "a-b.c--", null)]
	[InlineData("1.0.0--", 1, 0, 0, "-", null)]
	[InlineData("1.0.0-01a", 1, 0, 0, "01a", null)]
	[InlineData("1.2.3+build", 1, 2, 3, null, "build")]
	[InlineData("1.2.3+001.0-x", 1, 2, 3, null, "001.0-x")]
	[InlineData("1.2.3-rc.1+sha.7120b4a", 1, 2, 3, "rc.1", "sha.7120b4a")]
	[InlineData("1.2.3-a+b-c", 1, 2, 3, "a", "b-c")]
	[InlineData("2147483647.2147483647.2147483647", int.MaxValue, int.MaxValue, int.MaxValue, null, null)]
	public static void ParsesValidStrings(string s, int maj, int min, int patch, string? pre, string? build) {
		var v = Semver.Parse(s);
		Assert.Equal(maj, v.Major);
		Assert.Equal(min, v.Minor);
		Assert.Equal(patch, v.Patch);
		Assert.Equal(pre, v.Prerelease);
		Assert.Equal(build, v.BuildMetadata);
		Assert.Equal(pre is not null, v.IsPrerelease);
		Assert.Equal(s, v.ToString());

		Assert.True(Semver.TryParse(s, out Semver t));
		Assert.Equal(v, t);
		Assert.True(Semver.TryParse(s.AsSpan(), out Semver u));
		Assert.Equal(v, u);
		Assert.Equal(v, Semver.Parse(s.AsSpan()));
	}

	[Theory]
	[InlineData("")]
	[InlineData("1")]
	[InlineData("1.2")]
	[InlineData("1.2.3.4")]
	[InlineData("01.2.3")]
	[InlineData("1.02.3")]
	[InlineData("1.2.03")]
	[InlineData("-1.2.3")]
	[InlineData("+1.2.3")]
	[InlineData("v1.2.3")]
	[InlineData(" 1.2.3")]
	[InlineData("1.2.3 ")]
	[InlineData("1.2.3\n")]
	[InlineData("1.2.3-a\n")]
	[InlineData("1.2.3+b\n")]
	[InlineData("1.2.3-")]
	[InlineData("1.2.3+")]
	[InlineData("1.2.3-+b")]
	[InlineData("1.2.3-a.")]
	[InlineData("1.2.3-.a")]
	[InlineData("1.2.3-a..b")]
	[InlineData("1.2.3-01")]
	[InlineData("1.2.3-a.00")]
	[InlineData("1.2.3-a_b")]
	[InlineData("1.2.3+a..b")]
	[InlineData("1.2.3+a+b")]
	[InlineData("1.2.3+é")]
	[InlineData("１.2.3")]
	[InlineData("2147483648.0.0")]
	[InlineData("0.2147483648.0")]
	[InlineData("0.0.2147483648")]
	public static void DoesntParseInvalidStrings(string s) {
		Assert.False(Semver.TryParse(s, out Semver v));
		Assert.Equal(default, v);
		Assert.False(Semver.TryParse(s.AsSpan(), out _));
		Assert.Throws<FormatException>(() => Semver.Parse(s));
		Assert.Throws<FormatException>(() => Semver.Parse(s.AsSpan()));
	}

	[Fact]
	public static void DoesntParseNull() {
		Assert.False(Semver.TryParse(null, out _));
		Assert.False(Semver.TryParse(null, null, out _));
		Assert.Throws<ArgumentNullException>(static () => Semver.Parse(null));
		Assert.Throws<ArgumentNullException>(static () => Semver.Parse(null, null));
	}

	[Fact]
	public static void GenericISpanParsableWorks() {
		Semver v = parseGeneric<Semver>("1.2.3-rc.1+b".AsSpan());
		Assert.Equal(new Semver(1, 2, 3, "rc.1", "b"), v);
		Assert.False(tryParseGeneric<Semver>("1.2".AsSpan(), out _));
	}

	private static T parseGeneric<T>(ReadOnlySpan<char> s) where T : ISpanParsable<T> => T.Parse(s, null);
	private static bool tryParseGeneric<T>(ReadOnlySpan<char> s, out T? val) where T : ISpanParsable<T> =>
		T.TryParse(s, null, out val);

	[Fact]
	public static void ConstructorRejectsNegative() {
		Assert.Throws<ArgumentOutOfRangeException>(static () => new Semver(-1, 0, 0));
		Assert.Throws<ArgumentOutOfRangeException>(static () => new Semver(0, -1, 0));
		Assert.Throws<ArgumentOutOfRangeException>(static () => new Semver(0, 0, -1));
	}

#pragma warning disable CA1507 // use nameof in place of string literal
	[Theory]
	[InlineData("")]
	[InlineData(".")]
	[InlineData("a.")]
	[InlineData(".a")]
	[InlineData("a..b")]
	[InlineData("01")]
	[InlineData("a.00")]
	[InlineData("a_b")]
	[InlineData("a+b")]
	public static void ConstructorRejectsInvalidPrerelease(string pre) =>
		Assert.Throws<ArgumentException>("pre", () => new Semver(1, 0, 0, pre: pre));

	[Theory]
	[InlineData("")]
	[InlineData(".")]
	[InlineData("a.")]
	[InlineData(".a")]
	[InlineData("a..b")]
	[InlineData("a_b")]
	[InlineData("a+b")]
	public static void ConstructorRejectsInvalidBuild(string build) =>
		Assert.Throws<ArgumentException>("build", () => new Semver(1, 0, 0, build: build));
#pragma warning restore CA1507 // use nameof in place of string literal

	[Theory]
	[InlineData("0")]
	[InlineData("01")]
	[InlineData("a.00")]
	[InlineData("-")]
	public static void ConstructorAcceptsBuild(string build) => Assert.Equal(build, new Semver(1, 0, 0, build: build).BuildMetadata);

	[Theory]
	// M.y.z, M > 0: major must match
	[InlineData("1.2.3", "1.2.3", true)]
	[InlineData("1.2.3", "1.2.4", true)]
	[InlineData("1.2.3", "1.3.0", true)]
	[InlineData("1.2.3", "1.2.2", false)]
	[InlineData("1.2.3", "1.1.9", false)]
	[InlineData("1.2.3", "2.0.0", false)]
	[InlineData("1.2.3", "0.9.9", false)]
	[InlineData("1.0.0", "1.999.999", true)]
	// 0.m.z, m > 0: major and minor must match
	[InlineData("0.2.3", "0.2.3", true)]
	[InlineData("0.2.3", "0.2.9", true)]
	[InlineData("0.2.3", "0.2.2", false)]
	[InlineData("0.2.3", "0.3.0", false)]
	[InlineData("0.2.3", "1.2.3", false)]
	[InlineData("0.2.0", "0.2.0", true)]
	// 0.0.p: major, minor, and patch must match
	[InlineData("0.0.3", "0.0.3", true)]
	[InlineData("0.0.3", "0.0.4", false)]
	[InlineData("0.0.3", "0.1.3", false)]
	[InlineData("0.0.3", "1.0.3", false)]
	[InlineData("0.0.0", "0.0.0", true)]
	[InlineData("0.0.0", "0.0.1", false)]
	[InlineData("0.0.0", "0.1.0", false)]
	[InlineData("0.0.0", "1.0.0", false)]
	// given is a prerelease: minimum must also be a prerelease and have same major/minor/patch
	[InlineData("1.2.3-alpha", "1.2.3-alpha", true)]
	[InlineData("1.2.3-alpha", "1.2.3-beta", true)]
	[InlineData("1.2.3-beta", "1.2.3-alpha", false)]
	[InlineData("1.2.3-alpha", "1.2.4-alpha", false)]
	[InlineData("1.2.3-alpha", "1.3.0-alpha", false)]
	[InlineData("1.2.3", "1.2.3-alpha", false)]
	[InlineData("1.2.3", "1.2.4-alpha", false)]
	[InlineData("1.2.2", "1.2.3-alpha", false)]
	[InlineData("0.0.0", "0.0.0-alpha", false)]
	[InlineData("0.0.3-alpha", "0.0.3-beta", true)]
	// prerelease minimum, release given
	[InlineData("1.2.3-alpha", "1.2.3", true)]
	[InlineData("1.2.3-alpha", "1.5.0", true)]
	[InlineData("1.2.3-alpha", "2.0.0", false)]
	[InlineData("0.2.3-alpha", "0.2.3", true)]
	[InlineData("0.2.3-alpha", "0.2.9", true)]
	[InlineData("0.2.3-alpha", "0.3.0", false)]
	[InlineData("0.0.3-alpha", "0.0.3", true)]
	[InlineData("0.0.3-alpha", "0.0.4", false)]
	// build metadata ignored
	[InlineData("1.2.3+a", "1.2.3+b", true)]
	[InlineData("1.2.3+a", "1.2.3", true)]
	[InlineData("1.2.3", "1.2.3+a", true)]
	[InlineData("1.2.4+a", "1.2.3+b", false)]
	[InlineData("1.2.3-alpha+a", "1.2.3-alpha+b", true)]
	[InlineData("0.0.0+a", "0.0.0+b", true)]
	public static void Compatible(string minimum, string given, bool expected) =>
		Assert.Equal(expected, Semver.Compatible(Semver.Parse(minimum), Semver.Parse(given)));

	public static TheoryData<string, string> PrecedenceChain => new() {
		{ "1.0.0-alpha", "1.0.0-alpha.1" },
		{ "1.0.0-alpha.1", "1.0.0-alpha.beta" },
		{ "1.0.0-alpha.beta", "1.0.0-beta" },
		{ "1.0.0-beta", "1.0.0-beta.2" },
		{ "1.0.0-beta.2", "1.0.0-beta.11" },
		{ "1.0.0-beta.11", "1.0.0-rc.1" },
		{ "1.0.0-1", "1.0.0-2" },
		{ "1.0.0-2", "1.0.0-10" },
		{ "1.0.0-9", "1.0.0-a" },
		{ "1.0.0-A", "1.0.0-a" },
		{ "1.0.0-a-", "1.0.0-a-b" },
	};

	[Theory]
	[MemberData(nameof(PrecedenceChain))]
	public static void CompatibleFollowsPrereleasePrecedence(string lower, string higher) {
		var lo = Semver.Parse(lower);
		var hi = Semver.Parse(higher);
		Assert.True(Semver.Compatible(lo, hi));
		Assert.False(Semver.Compatible(hi, lo));
	}

	[Fact]
	public static void EqualityIncludesBuildMetadata() {
		var a = Semver.Parse("1.2.3-rc.1+x");
		Semver b = new(1, 2, 3, "rc.1", "x");
		var c = Semver.Parse("1.2.3-rc.1+y");
		var d = Semver.Parse("1.2.3-rc.1");

		Assert.True(a.Equals(b));
		Assert.True(a == b);
		Assert.False(a != b);
		Assert.True(a.Equals((object)b));
		Assert.Equal(a.GetHashCode(), b.GetHashCode());

		Assert.False(a.Equals(c));
		Assert.False(a == c);
		Assert.True(a != c);
		Assert.False(a.Equals(d));
		Assert.False(a.Equals(null));
		Assert.False(a.Equals("1.2.3-rc.1+x"));
	}

	[Theory]
	[InlineData("1.2.3", "1.2.3", true)]
	[InlineData("1.2.3+a", "1.2.3+b", true)]
	[InlineData("1.2.3+a", "1.2.3", true)]
	[InlineData("1.2.3-rc.1+a", "1.2.3-rc.1+b", true)]
	[InlineData("1.2.3-rc.1", "1.2.3", false)]
	[InlineData("1.2.3-rc.1", "1.2.3-rc.2", false)]
	[InlineData("1.2.3-RC", "1.2.3-rc", false)]
	[InlineData("1.2.3+a", "1.2.4+a", false)]
	[InlineData("1.2.3", "1.3.3", false)]
	[InlineData("1.2.3", "2.2.3", false)]
	public static void IgnoreBuildComparerWorks(string left, string right, bool expected) {
		SemverIgnoreBuildEqualityComparer cmp = SemverIgnoreBuildEqualityComparer.Instance;
		var l = Semver.Parse(left);
		var r = Semver.Parse(right);
		Assert.Equal(expected, cmp.Equals(l, r));
		Assert.Equal(expected, cmp.Equals(r, l));
		if (expected)
			Assert.Equal(cmp.GetHashCode(l), cmp.GetHashCode(r));
	}

	[Fact]
	public static void IgnoreBuildComparerWorksInAHashSet() {
		HashSet<Semver> set = new(SemverIgnoreBuildEqualityComparer.Instance) {
			Semver.Parse("1.2.3+a"),
			Semver.Parse("1.2.3+b"),
			Semver.Parse("1.2.3"),
			Semver.Parse("1.2.3-rc.1+a"),
		};
		Assert.Equal(2, set.Count);
	}

	[Theory]
	[InlineData("1.2.3")]
	[InlineData("0.0.0-rc.1+sha.7120b4a")]
	public static void JsonRoundtrips(string s) {
		var v = Semver.Parse(s);
		string json = JsonSerializer.Serialize(v);
		Assert.Equal(JsonSerializer.Serialize(s), json);
		Assert.Equal(v, JsonSerializer.Deserialize<Semver>(json));
	}

	[Fact]
	public static void JsonInvalidInputIsRejected() {
		Assert.Throws<FormatException>(static () => JsonSerializer.Deserialize<Semver>("\"1.2\""));
		Assert.Throws<ArgumentNullException>(static () => JsonSerializer.Deserialize<Semver>("null"));
	}
}
