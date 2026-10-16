// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Assets;
using Injure.Draw;
using Injure.Draw.Text;

namespace Injure.Analyzers.Tests.Assets;

public sealed class AssetCatalogGeneratorTests {
	private const string catalog = """
		using Injure.Assets;
		namespace Game;
		[AssetCatalog("assets", "game")]
		public sealed partial class GameAssets;
		""";

	private static object prop(object o, string name) =>
		o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o)
			?? throw new Xunit.Sdk.XunitException($"{o.GetType().Name} has no property '{name}'");

	private static bool hasProp(Type t, string name) => t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not null;

	private static object construct(Gen.Result r, AssetStore store) {
		Assert.Empty(r.CompileErrors);
		Type t = r.Assembly!.GetType("Game.GameAssets")!;
		return Activator.CreateInstance(t, store)!;
	}

	[Fact]
	public static void DirsBecomeNestedGroupsWithTypedAssets() {
		Gen.Result r = Gen.Run(catalog, [
			"assets/player.png",
			"assets/ui.tex.json",
			"assets/textures/bg_01.png",
			"assets/textures/characters/hero-idle.png",
			"assets/fonts/main.ttf",
			"assets/.DS_Store",
			"assets/.git/ignored.png",
			"elsewhere/not-included.png",
		]);
		Assert.DoesNotContain(r.GeneratorDiagnostics, static d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
		using AssetStore store = new();
		object assets = construct(r, store);

		Asset<Texture2d> player = Assert.IsType<Asset<Texture2d>>(prop(assets, "Player"));
		Assert.Equal(new AssetId("game", "player.png"), player.AssetId);
		Assert.Same(store.GetAsset<Texture2d>(new AssetId("game", "player.png")), player);
		Assert.IsType<Asset<Texture2d>>(prop(assets, "Ui"));
		Asset<Font> font = Assert.IsType<Asset<Font>>(prop(prop(assets, "Fonts"), "Main"));
		Assert.Equal(new AssetId("game", "fonts/main.ttf"), font.AssetId);

		object textures = prop(assets, "Textures");
		Assert.Equal("TexturesGroup", textures.GetType().Name);
		Asset<Texture2d> hero = Assert.IsType<Asset<Texture2d>>(prop(prop(textures, "Characters"), "HeroIdle"));
		Assert.Equal(new AssetId("game", "textures/characters/hero-idle.png"), hero.AssetId);
		Assert.Equal("textures/bg_01.png", ((Asset<Texture2d>)prop(textures, "Bg01")).AssetId.Path);

		// groups include their subdirectories, and the catalog is the group of everything
		Assert.Equal(2, ((AssetGroup)textures).Members.Count);
		Assert.Equal(5, ((AssetGroup)assets).Members.Count);
		Assert.Contains(hero, ((AssetGroup)assets).Members);
	}

	[Fact]
	public static void FileKindsCanBeAddedAndTheLongestSuffixWins() {
		Gen.Result r = Gen.Run(
			"""
			using Injure.Assets;
			[assembly: AssetFileKind(".json", typeof(Game.Data))]
			[assembly: AssetFileKind(".level.json", typeof(Game.Level))]
			namespace Game;
			public sealed class Data;
			public sealed class Level;
			[AssetCatalog("assets", "game")]
			public sealed partial class GameAssets;
			""",
			["assets/config.json", "assets/forest.level.json", "assets/ui.tex.json", "assets/UPPER.PNG"]
		);
		using AssetStore store = new();
		object assets = construct(r, store);
		Assert.Equal("Data", prop(assets, "Config").GetType().GetGenericArguments()[0].Name);
		Assert.Equal("Level", prop(assets, "Forest").GetType().GetGenericArguments()[0].Name);
		Assert.IsType<Asset<Texture2d>>(prop(assets, "Ui"));
		Assert.IsType<Asset<Texture2d>>(prop(assets, "UPPER"));
	}

	[Fact]
	public static void FileKindsCanReplaceBuiltinOnes() {
		Gen.Result r = Gen.Run(
			"""
			using Injure.Assets;
			[assembly: AssetFileKind(".png", typeof(Game.Sprite))]
			namespace Game;
			public sealed class Sprite;
			[AssetCatalog("assets", "game")]
			public sealed partial class GameAssets;
			""",
			["assets/hero.png"]
		);
		using AssetStore store = new();
		Assert.Equal("Sprite", prop(construct(r, store), "Hero").GetType().GetGenericArguments()[0].Name);
	}

	[Fact]
	public static void CollidingNamesAreLeftOutWithAnError() {
		Gen.Result r = Gen.Run(catalog, [
			"assets/player.png",
			"assets/player.tex.json",
			"assets/hud.png",
			"assets/hud/icon.png",
			"assets/members.png",
			"assets/x/a.png",
			"assets/x-group.png",
			"assets/fine.png",
		]);
		Microsoft.CodeAnalysis.Diagnostic[] collisions = r.WithId("IJ0201");
		Assert.Equal(4, collisions.Length);
		Assert.All(collisions, static d => Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity));
		Assert.Contains(collisions, static d => d.GetMessage().Contains("'player.png', 'player.tex.json'"));
		using AssetStore store = new();
		object assets = construct(r, store);
		Type t = assets.GetType();
		foreach (string gone in (string[])["Player", "Hud", "X", "XGroup"])
			Assert.False(hasProp(t, gone), gone);
		Assert.True(hasProp(t, "Fine"));
		Assert.Single(((AssetGroup)assets).Members);
	}

	[Fact]
	public static void UnmappableAndUnknownFilesAreReported() {
		Gen.Result r = Gen.Run(catalog, ["assets/---.png", "assets/notes.txt", "assets/data.bin", "assets/ok.png"]);
		Assert.Single(r.WithId("IJ0202"));
		Microsoft.CodeAnalysis.Diagnostic unknown = Assert.Single(r.WithId("IJ0203"));
		Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, unknown.Severity);
		Assert.Contains("2 file(s)", unknown.GetMessage());
		using AssetStore store = new();
		Assert.Single(((AssetGroup)construct(r, store)).Members);
	}

	[Fact]
	public static void MisconfigurationIsReported() {
		Assert.Single(Gen.Run(catalog, ["other/a.png"]).WithId("IJ0200"));
		Assert.Single(Gen.Run(catalog, ["assets/a.png"], withProjectDir: false).WithId("IJ0204"));
		foreach (string bad in (string[])[
			"[AssetCatalog(\"assets\", \"game\")] public sealed class NotPartial;",
			"public sealed class Outer { [AssetCatalog(\"assets\", \"game\")] public sealed partial class Nested; }",
			"public class Base; [AssetCatalog(\"assets\", \"game\")] public sealed partial class Derived : Base;",
		])
			Assert.Single(Gen.Run("using Injure.Assets; namespace Game; " + bad, ["assets/a.png"]).WithId("IJ0205"));
	}

	[Fact]
	public static void InvalidFileKindsAreReported() {
		Gen.Result r = Gen.Run(
			"""
			using Injure.Assets;
			[assembly: AssetFileKind("png", typeof(Game.A))]
			[assembly: AssetFileKind(".lvl", typeof(Game.A))]
			[assembly: AssetFileKind(".LVL", typeof(Game.A))]
			namespace Game;
			public sealed class A;
			""",
			[]
		);
		Assert.Equal(2, r.WithId("IJ0206").Length);
	}
}
