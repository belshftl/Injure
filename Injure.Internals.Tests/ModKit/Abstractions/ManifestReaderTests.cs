// SPDX-License-Identifier: MIT

using System;

using Injure.ModKit.Abstractions;
using Injure.ModKit.Abstractions.ManifestReader;

namespace Injure.Internals.Tests.ModKit.Abstractions;

public sealed class ManifestReaderTests {
	private static ModManifest parse(string json) => ManifestReader.Parse(new SourceText("<test manifest>", json));
	private static ManifestReadException parseError(string json) => Assert.Throws<ManifestReadException>(() => parse(json));

	private static string validCodeManifest(
		string extra = "",
		string relationships = "[]",
		string assets = """{ "management": "none" }""",
		string nativeLibraries = "[]"
	) => $$"""
{
	"schema": 0,
	"type": "code",
	"id": "jdoe.test-mod",
	"version": "1.2.3",
	"reloadable": true,
	"live-reloadable": true,
	"display-name": "Test Mod",
	"description": "A test mod.",
	"license-spdx": "MIT",
	"game": {
		"target-version": "9.8.7",
		"target-build-mvid": "11111111-2222-3333-4444-555555555555"
	},
	"relationships": {{relationships}},
	"assets": {{assets}},
	"native-libraries": {{nativeLibraries}},
	"entry-assembly": "bin/TestMod.dll"{{extra}}
}
""";

	private static string validContentManifest(
		string extra = "",
		string relationships = "[]",
		string assets = """{ "management": "none" }""",
		string nativeLibraries = "[]"
	) => $$"""
{
	"schema": 0,
	"type": "content",
	"id": "jdoe.content-mod",
	"version": "1.2.3",
	"reloadable": false,
	"display-name": "Content Mod",
	"description": "A content mod.",
	"license-spdx": "LicenseRef-Proprietary",
	"game": {},
	"relationships": {{relationships}},
	"assets": {{assets}},
	"native-libraries": {{nativeLibraries}}{{extra}}
}
""";

	[Fact]
	public void ParsesCodeManifest() {
		ModManifest manifest = parse(validCodeManifest());
		CodeModManifest code = Assert.IsType<CodeModManifest>(manifest);
		Assert.Equal("jdoe.test-mod", code.OwnerID);
		Assert.Equal(Semver.Parse("1.2.3"), code.Version);
		Assert.True(code.Reloadable);
		Assert.True(code.LiveReloadable);
		Assert.Equal("Test Mod", code.DisplayName);
		Assert.Equal("A test mod.", code.Description);
		Assert.Equal("MIT", code.LicenseSpdx);
		Assert.Equal(Semver.Parse("9.8.7"), code.Game.TargetVersion);
		Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), code.Game.TargetBuildMvid);
		Assert.Empty(code.Relationships);
		Assert.Equal(ModAssetManagementKind.None, code.Assets.ManagementKind);
		Assert.Null(code.Assets.Root);
		Assert.Empty(code.NativeLibraries);
		Assert.Equal("bin/TestMod.dll", code.EntryAssembly);
	}

	[Fact]
	public void ParsesContentManifest() {
		ModManifest manifest = parse(validContentManifest());
		ContentModManifest content = Assert.IsType<ContentModManifest>(manifest);
		Assert.Equal("jdoe.content-mod", content.OwnerID);
		Assert.Equal(Semver.Parse("1.2.3"), content.Version);
		Assert.False(content.Reloadable);
		Assert.Equal("Content Mod", content.DisplayName);
		Assert.Equal("A content mod.", content.Description);
		Assert.Equal("LicenseRef-Proprietary", content.LicenseSpdx);
		Assert.Null(content.Game.TargetVersion);
		Assert.Null(content.Game.TargetBuildMvid);
		Assert.Empty(content.Relationships);
		Assert.Equal(ModAssetManagementKind.None, content.Assets.ManagementKind);
		Assert.Null(content.Assets.Root);
		Assert.Empty(content.NativeLibraries);
	}

	[Fact]
	public void MissingOptionalSectionsDefaultToEmptyOrNone() {
		string json = """
{
	"schema": 0,
	"type": "content",
	"id": "jdoe.minimal",
	"version": "1.0.0"
}
""";
		ContentModManifest manifest = Assert.IsType<ContentModManifest>(parse(json));
		Assert.False(manifest.Reloadable);
		Assert.Null(manifest.DisplayName);
		Assert.Null(manifest.Description);
		Assert.Null(manifest.LicenseSpdx);
		Assert.Null(manifest.Game.TargetVersion);
		Assert.Null(manifest.Game.TargetBuildMvid);
		Assert.Empty(manifest.Relationships);
		Assert.Equal(ModAssetManagementKind.None, manifest.Assets.ManagementKind);
		Assert.Null(manifest.Assets.Root);
		Assert.Empty(manifest.NativeLibraries);
	}

	[Fact]
	public void AcceptsCommentsAndTrailingCommas() {
		string json = """
{
	// this is a comment
	"schema": 0,
	"type": "content",
	"id": "jdoe.comments",
	"version": "1.0.0",
	"relationships": [
	],
}
""";
		ContentModManifest manifest = Assert.IsType<ContentModManifest>(parse(json));
		Assert.Equal("jdoe.comments", manifest.OwnerID);
	}

	[Theory]
	[InlineData("code", typeof(CodeModManifest))]
	[InlineData("content", typeof(ContentModManifest))]
	public void EnumValuesMustBeKebabCase(string type, Type expectedManifestType) {
		string json = $$"""
{
	"schema": 0,
	"type": "{{type}}",
	"id": "jdoe.kebab",
	"version": "1.0.0"{{(type == "code" ? @", ""entry-assembly"": ""Mod.dll""" : "")}}
}
""";
		Assert.IsType(expectedManifestType, parse(json));
	}

	[Theory]
	[InlineData("Code")]
	[InlineData("CODE")]
	[InlineData("co_de")]
	[InlineData("cod-e")]
	public void RejectsInvalidEnumSpellings(string type) {
		string json = $$"""
{
	"schema": 0,
	"type": "{{type}}",
	"id": "jdoe.invalid-enum",
	"version": "1.0.0",
	"entry-assembly": "Mod.dll"
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.type", ex.JsonNodePath);
		Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void RejectsUnknownRootProperty() {
		ManifestReadException ex = parseError(
			validContentManifest(
				extra: @",
	""typo"": true
"
			)
		);
		Assert.Equal("$.typo", ex.JsonNodePath);
		Assert.Contains("unknown property", ex.Message);
	}

	[Fact]
	public void RejectsUnsupportedSchema() {
		string json = """
{
	"schema": 2,
	"type": "content",
	"id": "jdoe.unsupported-schema",
	"version": "1.0.0"
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.schema", ex.JsonNodePath);
		Assert.Contains("unsupported schema", ex.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("foo@bar")]
	[InlineData("Foo Bar")]
	public void RejectsInvalidOwnerID(string id) {
		string json = $$"""
{
	"schema": 0,
	"type": "content",
	"id": "{{id}}",
	"version": "1.0.0"
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.id", ex.JsonNodePath);
		Assert.Contains("owner ID", ex.Message);
	}

	[Fact]
	public void RejectsBadSemver() {
		string json = """
{
	"schema": 0,
	"type": "content",
	"id": "jdoe.bad-semver",
	"version": "foobar"
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.version", ex.JsonNodePath);
		Assert.Contains("invalid version", ex.Message);
	}

	[Fact]
	public void RejectsLiveReloadableWithoutReloadable() {
		string json = """
{
	"schema": 0,
	"type": "code",
	"id": "jdoe.bad-live",
	"version": "1.0.0",
	"reloadable": false,
	"live-reloadable": true,
	"entry-assembly": "Mod.dll"
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.live-reloadable", ex.JsonNodePath);
		Assert.Contains("only valid together with reloadable = true", ex.Message);
	}

	[Fact]
	public void RejectsLiveReloadableOnContentMods() {
		string json = """
{
	"schema": 0,
	"type": "content",
	"id": "jdoe.bad-content-live",
	"version": "1.0.0",
	"reloadable": true,
	"live-reloadable": false
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.live-reloadable", ex.JsonNodePath);
		Assert.Contains("only valid for code mods", ex.Message);
	}

	[Fact]
	public void RejectsEntryAssemblyOnContentMods() {
		ManifestReadException ex = parseError(
			validContentManifest(
				extra: @",
	""entry-assembly"": ""Mod.dll""
"
			)
		);
		Assert.Equal("$.entry-assembly", ex.JsonNodePath);
		Assert.Contains("only valid for code mods", ex.Message);
	}

	[Fact]
	public void RejectsMissingEntryAssemblyOnCodeMods() {
		string json = """
{
	"schema": 0,
	"type": "code",
	"id": "jdoe.no-entry",
	"version": "1.0.0"
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.entry-assembly", ex.JsonNodePath);
		Assert.Contains("required property", ex.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" leading.dll")]
	[InlineData("trailing.dll ")]
	[InlineData("/absolute.dll")]
	[InlineData("C:\\absolute.dll")]
	[InlineData("dir\\file.dll")]
	[InlineData("dir//file.dll")]
	[InlineData("./file.dll")]
	[InlineData("dir/./file.dll")]
	[InlineData("../file.dll")]
	[InlineData("dir/../file.dll")]
	[InlineData("dir/file.dll/")]
	[InlineData("dir/\u0001/file.dll")]
	public void RejectsInvalidPaths(string entryAssembly) {
		string json = $$"""
{
	"schema": 0,
	"type": "code",
	"id": "jdoe.invalid-path",
	"version": "1.0.0",
	"entry-assembly": "{{jsonEscape(entryAssembly)}}"
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.entry-assembly", ex.JsonNodePath);
		Assert.Contains("path", ex.Message);
	}

	[Fact]
	public void AllowsUnicodeRelativePath() {
		string json = """
{
	"schema": 0,
	"type": "code",
	"id": "jdoe.unicode-path",
	"version": "1.0.0",
	"entry-assembly": "bin/åäö/Mod.dll"
}
""";
		CodeModManifest manifest = Assert.IsType<CodeModManifest>(parse(json));
		Assert.Equal("bin/åäö/Mod.dll", manifest.EntryAssembly);
	}

	[Theory]
	[InlineData("tracked", "assets")]
	[InlineData("manual", "content/assets")]
	public void ParsesAssetRootWhenRequired(string management, string root) {
		string json = validContentManifest(
			assets: $$"""
{
	"management": "{{management}}",
	"root": "{{root}}"
}
"""
		);
		ContentModManifest manifest = Assert.IsType<ContentModManifest>(parse(json));
		Assert.Equal(root, manifest.Assets.Root);
	}

	[Theory]
	[InlineData("tracked")]
	[InlineData("manual")]
	public void RejectsMissingAssetRootWhenRequired(string management) {
		string json = validContentManifest(
			assets: $$"""
{
	"management": "{{management}}"
}
"""
		);
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.assets.management", ex.JsonNodePath);
		Assert.Contains("assets.root is required", ex.Message);
	}

	[Fact]
	public void RejectsAssetRootWhenManagementIsNone() {
		string json = validContentManifest(
			assets: """
{
	"management": "none",
	"root": "assets"
}
"""
		);
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.assets.root", ex.JsonNodePath);
		Assert.Contains("must be absent", ex.Message);
	}

	[Fact]
	public void RejectsInvalidAssetRootPath() {
		string json = validContentManifest(
			assets: """
{
	"management": "tracked",
	"root": "assets/../invalid"
}
"""
		);
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.assets.root", ex.JsonNodePath);
		Assert.Contains("..", ex.Message);
	}

	[Fact]
	public void ParsesRelationships() {
		string relationships = """
[
	{
		"id": "other.required",
		"kind": "requires-self-after",
		"version": "2.0.0",
		"description": "Required API."
	},
	{
		"id": "other.optional",
		"kind": "if-present-self-before",
		"description": "Optional integration with feature XYZ."
	},
	{
		"id": "other.conflict",
		"kind": "conflicts",
		"version": "3.0.0",
		"description": "Both replace system XYZ in conflicting ways."
	}
]
""";
		ContentModManifest manifest = Assert.IsType<ContentModManifest>(parse(validContentManifest(relationships: relationships)));
		Assert.Collection(
			manifest.Relationships,
			rel => {
				Assert.Equal("other.required", rel.OwnerID);
				Assert.Equal(ModRelationshipKind.RequiresSelfAfter, rel.Kind);
				Assert.Equal(Semver.Parse("2.0.0"), rel.Version);
				Assert.Equal("Required API.", rel.Description);
			},
			rel => {
				Assert.Equal("other.optional", rel.OwnerID);
				Assert.Equal(ModRelationshipKind.IfPresentSelfBefore, rel.Kind);
				Assert.Null(rel.Version);
				Assert.Equal("Optional integration with feature XYZ.", rel.Description);
			},
			rel => {
				Assert.Equal("other.conflict", rel.OwnerID);
				Assert.Equal(ModRelationshipKind.Conflicts, rel.Kind);
				Assert.Equal(Semver.Parse("3.0.0"), rel.Version);
				Assert.Equal("Both replace system XYZ in conflicting ways.", rel.Description);
			}
		);
	}

	[Fact]
	public void RejectsRequiredRelationshipWithoutVersion() {
		string relationships = """
[
	{
		"id": "other.required",
		"kind": "requires-self-after"
	}
]
""";
		ManifestReadException ex = parseError(validContentManifest(relationships: relationships));
		Assert.Equal("$.relationships[0].version", ex.JsonNodePath);
		Assert.Contains("required property", ex.Message);
	}

	[Fact]
	public void RejectsDuplicateRelationshipWithSameOwnerAndKind() {
		string relationships = """
[
	{
		"id": "other.mod",
		"kind": "if-present-self-after"
	},
	{
		"id": "other.mod",
		"kind": "if-present-self-after"
	}
]
""";
		ManifestReadException ex = parseError(validContentManifest(relationships: relationships));
		Assert.Equal("$.relationships[1].id", ex.JsonNodePath);
		Assert.Contains("duplicate relationship", ex.Message);
	}

	[Fact]
	public void ParsesNativeLibrariesOnNonReloadableMod() {
		string nativeLibraries = """
[
	{
		"id": "libwebp",
		"path": "native/linux-x64/libwebp.so",
		"rid": "linux-x64"
	},
	{
		"id": "libwebp",
		"path": "native/win-x64/webp.dll",
		"rid": "win-x64"
	}
]
""";
		ContentModManifest manifest = Assert.IsType<ContentModManifest>(parse(validContentManifest(nativeLibraries: nativeLibraries)));
		Assert.Collection(
			manifest.NativeLibraries,
			lib => {
				Assert.Equal("libwebp", lib.ID);
				Assert.Equal("native/linux-x64/libwebp.so", lib.Path);
				Assert.Equal("linux-x64", lib.RuntimeIdentifier);
			},
			lib => {
				Assert.Equal("libwebp", lib.ID);
				Assert.Equal("native/win-x64/webp.dll", lib.Path);
				Assert.Equal("win-x64", lib.RuntimeIdentifier);
			}
		);
	}

	[Fact]
	public void RejectsNativeLibrariesOnReloadableMod() {
		string nativeLibraries = """
[
	{
		"id": "libwebp",
		"path": "libwebp.so",
		"rid": "linux-x64"
	}
]
""";
		ManifestReadException ex = parseError(validCodeManifest(nativeLibraries: nativeLibraries));
		Assert.Equal("$.native-libraries", ex.JsonNodePath);
		Assert.Contains("must be empty for reloadable", ex.Message);
	}

	[Fact]
	public void RejectsDuplicateNativeLibraryIDAndRid() {
		string nativeLibraries = """
[
	{
		"id": "libwebp",
		"rid": "linux-x64",
		"path": "a.so"
	},
	{
		"id": "libwebp",
		"path": "b.so",
		"rid": "linux-x64"
	}
]
""";
		ManifestReadException ex = parseError(validContentManifest(nativeLibraries: nativeLibraries));
		Assert.Equal("$.native-libraries[1].id", ex.JsonNodePath);
		Assert.Contains("duplicate native library", ex.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("bad id")]
	[InlineData("bad::id")]
	[InlineData("@bad")]
	public void RejectsInvalidNativeLibraryID(string id) {
		string nativeLibraries = $$"""
[
	{
		"id": "{{jsonEscape(id)}}",
		"path": "lib.so",
		"rid": "linux-x64"
	}
]
""";
		ManifestReadException ex = parseError(validContentManifest(nativeLibraries: nativeLibraries));
		Assert.Equal("$.native-libraries[0].id", ex.JsonNodePath);
		Assert.Contains("native library ID", ex.Message);
	}

	[Fact]
	public void RejectsEmptyNativeLibraryRid() {
		string nativeLibraries = """
[
	{
		"id": "libwebp",
		"path": "libwebp.so",
		"rid": ""
	}
]
""";
		ManifestReadException ex = parseError(validContentManifest(nativeLibraries: nativeLibraries));
		Assert.Equal("$.native-libraries[0].rid", ex.JsonNodePath);
		Assert.Contains("runtime identifier", ex.Message);
	}

	[Fact]
	public void ParsesGameCompatibility() {
		string json = validContentManifest(extra: "", assets: """{ "management": "none" }""").Replace(
			@"""game"": {}",
			"""
	"game": {
		"target-version": "5.6.7",
		"target-build-mvid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
	}
"""
		);
		ContentModManifest manifest = Assert.IsType<ContentModManifest>(parse(json));
		Assert.Equal(Semver.Parse("5.6.7"), manifest.Game.TargetVersion);
		Assert.Equal(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), manifest.Game.TargetBuildMvid);
	}

	[Fact]
	public void RejectsInvalidGameMvid() {
		string json = """
{
	"schema": 0,
	"type": "content",
	"id": "jdoe.invalid-mvid",
	"version": "1.0.0",
	"game": {
		"target-build-mvid": "not-a-guid"
	}
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.game.target-build-mvid", ex.JsonNodePath);
		Assert.Contains("invalid target-build-mvid", ex.Message);
	}

	[Fact]
	public void ExceptionCarriesLineAndSpan() {
		string json = """
{
	"schema": 0,
	"type": "content",
	"id": "foo@bar",
	"version": "1.0.0"
}
""";
		ManifestReadException ex = parseError(json);
		Assert.Equal("$.id", ex.JsonNodePath);
		Assert.Equal(3, ex.Span.Start.Line); // intentionally zero-based
		Assert.True(ex.Span.Start.Column > 0);
		Assert.True(ex.Span.End.Offset > ex.Span.Start.Offset);
	}

	private static string jsonEscape(string s) =>
		s.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal)
			.Replace("\b", "\\b", StringComparison.Ordinal)
			.Replace("\f", "\\f", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal)
			.Replace("\r", "\\r", StringComparison.Ordinal)
			.Replace("\t", "\\t", StringComparison.Ordinal)
			.Replace("\u0001", "\\u0001", StringComparison.Ordinal);
}
