// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public static class AssetIdTests {
	[Theory]
	[InlineData("injure")]
	[InlineData("my-mod.v2_x")]
	[InlineData("0start")]
	public static void ValidOwnerIdsAreValidNamespaces(string ns) => Assert.True(AssetId.ValidateNamespace(ns, out _));

	[Theory]
	[InlineData("")]
	[InlineData("has space")]
	[InlineData("-leading")]
	[InlineData("ünicode")]
	[InlineData("slash/ns")]
	public static void InvalidOwnerIdsAreInvalidNamespaces(string ns) {
		Assert.False(AssetId.ValidateNamespace(ns, out string? err));
		Assert.Contains("owner ID", err);
	}

	[Fact]
	public static void NamespacesFollowTheOwnerIdLengthLimit() {
		Assert.True(AssetId.ValidateNamespace(new string('a', 64), out _));
		Assert.False(AssetId.ValidateNamespace(new string('a', 65), out _));
	}

	[Theory]
	[InlineData("textures/player.png")]
	[InlineData("textures/my sprite ä.png")]
	[InlineData("a")]
	[InlineData("weird/[punct] (ok)/x@y#z+.txt")]
	public static void PathsArePermissive(string path) => Assert.True(AssetId.ValidatePath(path, out _));

	[Theory]
	[InlineData("")]
	[InlineData("/rooted")]
	[InlineData("trailing/")]
	[InlineData("a//b")]
	[InlineData("a/./b")]
	[InlineData("a/../b")]
	[InlineData("a\\b")]
	[InlineData(" leading")]
	[InlineData("trailing ")]
	[InlineData("a::b")]
	[InlineData("ctrl\u0001char")]
	public static void InvalidPathsAreRejected(string path) => Assert.False(AssetId.ValidatePath(path, out _));

	[Fact]
	public static void ParseRoundtripsThroughToString() {
		AssetId id = new("injure", "textures/my sprite.png");
		Assert.Equal("injure::textures/my sprite.png", id.ToString());
		Assert.Equal(id, AssetId.Parse(id.ToString()));
		Assert.True(AssetId.TryParse("a::b::c", out _) is false);
		Assert.Throws<FormatException>(() => AssetId.Parse("no separator"));
		Assert.Throws<ArgumentException>(() => AssetId.Parse("bad ns::path"));
	}

	[Fact]
	public static void EqualityIsCaseSensitive() =>
		Assert.NotEqual(new AssetId("injure", "Textures/A.png"), new AssetId("injure", "textures/a.png"));
}
