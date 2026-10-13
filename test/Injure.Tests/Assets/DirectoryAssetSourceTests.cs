// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;
using Injure.Mods;

namespace Injure.Tests.Assets;

public sealed class DirectoryAssetSourceTests {
	private sealed class TempDir : IDisposable {
		public string Path { get; } = Directory.CreateTempSubdirectory("injure-assets-").FullName;
		public void Dispose() => Directory.Delete(Path, recursive: true);
	}

	private static Asset<PlainValue> get(TestStore ts, string path) => ts.Store.GetAsset<PlainValue>(new AssetId("disk", path));

	[Fact]
	public static void PathsMustMatchTheFilesSpellingExactly() {
		using TempDir dir = new();
		Directory.CreateDirectory(Path.Combine(dir.Path, "Textures"));
		File.WriteAllText(Path.Combine(dir.Path, "Textures", "Player.txt"), "player");
		using TestStore ts = new();
		ts.Store.RegisterSource(new OwnerOrderedEntry<IAssetSource>(new DirectoryAssetSource("disk", dir.Path), TestStore.Ns, "disk"));
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();

		AssetBorrow<PlainValue> b = get(ts, "Textures/Player.txt").Borrow();
		Assert.Equal("player", b.Value.Text);
		Assert.Equal([new FileAssetDep(Path.Combine(dir.Path, "Textures", "Player.txt"))], b.Deps.ToArray());

		bool caseInsensitive = File.Exists(Path.Combine(dir.Path, "textures", "player.txt"));
		foreach (string wrong in (string[])["textures/Player.txt", "Textures/player.txt"]) {
			Asset<PlainValue> a = get(ts, wrong);
			if (caseInsensitive) {
				// the open succeeds here, but the same game would miss the file on Linux
				AssetLoadException ex = Assert.Throws<AssetLoadException>(() => a.Borrow());
				Assert.IsType<IOException>(ex.InnerException);
				Assert.Contains("casing or Unicode normalization differs", ex.InnerException.Message);
			} else {
				Assert.Throws<AssetUnhandledException>(() => a.Borrow());
			}
		}
		Assert.Throws<AssetUnhandledException>(() => get(ts, "Textures/missing.txt").Borrow());
	}

	[Fact]
	public static void PathsMustMatchTheFilesUnicodeNormalization() {
		using TempDir dir = new();
		string composed = "caf\u00e9.txt";
		string decomposed = "cafe\u0301.txt";
		File.WriteAllText(Path.Combine(dir.Path, composed), "coffee");
		using TestStore ts = new();
		ts.Store.RegisterSource(new OwnerOrderedEntry<IAssetSource>(new DirectoryAssetSource("disk", dir.Path), TestStore.Ns, "disk"));
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();

		Assert.Equal("coffee", get(ts, composed).Borrow().Value.Text);
		// macOS treats the two spellings as one name; most other filesystems don't
		if (File.Exists(Path.Combine(dir.Path, decomposed))) {
			AssetLoadException ex = Assert.Throws<AssetLoadException>(() => get(ts, decomposed).Borrow());
			Assert.IsType<IOException>(ex.InnerException);
		} else {
			Assert.Throws<AssetUnhandledException>(() => get(ts, decomposed).Borrow());
		}
	}
}
