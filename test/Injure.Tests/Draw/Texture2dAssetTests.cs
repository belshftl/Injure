// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Text;
using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Draw;
using Injure.Mods;
using Injure.Tests.Assets;
using Injure.Tests.Gpu;

namespace Injure.Tests.Draw;

public sealed class Texture2dAssetTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	// an uncompressed 24-bit BMP of the given size, all one color
	private static byte[] bmp(int width, int height) {
		int rowSize = (width * 3 + 3) & ~3;
		int imageSize = rowSize * height;
		using MemoryStream ms = new();
		using BinaryWriter w = new(ms);
		w.Write((byte)'B');
		w.Write((byte)'M');
		w.Write(54 + imageSize);
		w.Write(0);
		w.Write(54);
		w.Write(40);
		w.Write(width);
		w.Write(height);
		w.Write((short)1);
		w.Write((short)24);
		w.Write(0);
		w.Write(imageSize);
		w.Write(2835);
		w.Write(2835);
		w.Write(0);
		w.Write(0);
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				w.Write((byte)0x30);
				w.Write((byte)0x20);
				w.Write((byte)0x10);
			}
			for (int p = width * 3; p < rowSize; p++)
				w.Write((byte)0);
		}
		return ms.ToArray();
	}

	[Fact]
	public void ImagesAndTextureMetadataLoadThroughOneCreator() {
		using AssetStore store = new();
		MemorySource source = new();
		store.RegisterSource(new OwnerOrderedEntry<IAssetSource>(source, "test", "memory"));
		BuiltinAssetRegistrations.RegisterTexture2dInto(store, Device);
		using AssetThreadAttachment att = store.AttachCurrentThread();

		AssetId imgId = new("test", "img.bmp");
		AssetId subId = new("test", "sub.tex.json");
		AssetId badId = new("test", "bad.tex.json");
		AssetId notImgId = new("test", "notes.txt");
		source.Set(imgId, bmp(3, 2));
		source.Set(subId, Encoding.UTF8.GetBytes("""{"Source":"test::img.bmp","SourceRect":{"x":1,"y":0,"width":2,"height":2}}"""));
		source.Set(badId, Encoding.UTF8.GetBytes("""{"Source":"test::img.bmp","SourceRect":{"x":2,"y":0,"width":2,"height":2}}"""));
		source.Set(notImgId, Encoding.UTF8.GetBytes("not an image"));

		AssetBorrow<Texture2d> img = store.GetAsset<Texture2d>(imgId).Borrow();
		Assert.Equal((3u, 2u), (img.Value.Width, img.Value.Height));
		Assert.True(img.Value.IsAssetStoreOwned);

		AssetBorrow<Texture2d> sub = store.GetAsset<Texture2d>(subId).Borrow();
		Assert.Equal((2u, 2u), (sub.Value.Width, sub.Value.Height));
		// the metadata file and the image it refers to are both dependencies
		Assert.Equal(
			new HashSet<IAssetDep> { new MemoryDep(subId), new MemoryDep(imgId) },
			sub.Deps.ToArray().ToHashSet()
		);

		AssetLoadException bad = Assert.Throws<AssetLoadException>(() => store.GetAsset<Texture2d>(badId).Borrow());
		Assert.IsType<InvalidDataException>(bad.InnerException);
		Assert.Throws<AssetUnhandledException>(() => store.GetAsset<Texture2d>(notImgId).Borrow());
	}
}
