// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Text;
using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Draw;
using Injure.Gpu;
using Injure.Mods;
using Injure.Primitives;
using Injure.Tests.Assets;
using Injure.Tests.Gpu;

namespace Injure.Tests.Draw;

public sealed class SrgbTexture2dAssetTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	// an uncompressed flat-color 24-bit bmp of the given size
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
		BuiltinAssetRegistrations.RegisterSrgbTexture2dInto(store, Device);
		using AssetThreadAttachment att = store.AttachCurrentThread();

		AssetId imgId = new("test", "img.bmp");
		AssetId subId = new("test", "sub.tex.json");
		AssetId badId = new("test", "bad.tex.json");
		AssetId notImgId = new("test", "notes.txt");
		source.Set(imgId, bmp(3, 2));
		source.Set(subId, Encoding.UTF8.GetBytes("""{"Source":"test::img.bmp","SourceRect":{"x":1,"y":0,"width":2,"height":2}}"""));
		source.Set(badId, Encoding.UTF8.GetBytes("""{"Source":"test::img.bmp","SourceRect":{"x":2,"y":0,"width":2,"height":2}}"""));
		source.Set(notImgId, Encoding.UTF8.GetBytes("not an image"));

		AssetBorrow<SrgbTexture2d> img = store.GetAsset<SrgbTexture2d>(imgId).Borrow();
		Assert.Equal((3u, 2u), (img.Value.Width, img.Value.Height));
		Assert.True(img.Value.IsAssetStoreOwned);

		AssetBorrow<SrgbTexture2d> sub = store.GetAsset<SrgbTexture2d>(subId).Borrow();
		Assert.Equal((2u, 2u), (sub.Value.Width, sub.Value.Height));
		// the metadata file and the image it refers to are both dependencies
		Assert.Equal(
			new HashSet<IAssetDep> { new MemoryDep(subId), new MemoryDep(imgId) },
			sub.Deps.ToArray().ToHashSet()
		);

		AssetLoadException bad = Assert.Throws<AssetLoadException>(() => store.GetAsset<SrgbTexture2d>(badId).Borrow());
		Assert.IsType<InvalidDataException>(bad.InnerException);
		Assert.Throws<AssetUnhandledException>(() => store.GetAsset<SrgbTexture2d>(notImgId).Borrow());
	}

	// a 1x1 uncompressed 32-bit tga
	private static byte[] tga(RawColor32 c) => [
		0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 0x28,
		c.B, c.G, c.R, c.A,
	];

	[Fact]
	public void ImagesArePremultipliedOnLoad() {
		using AssetStore store = new();
		MemorySource source = new();
		store.RegisterSource(new OwnerOrderedEntry<IAssetSource>(source, "test", "memory"));
		BuiltinAssetRegistrations.RegisterSrgbTexture2dInto(store, Device);
		using AssetThreadAttachment att = store.AttachCurrentThread();
		AssetId id = new("test", "half.tga");
		source.Set(id, tga(new RawColor32(255, 255, 255, 128)));
		AssetBorrow<SrgbTexture2d> tex = store.GetAsset<SrgbTexture2d>(id).Borrow();

		// the target should get half of white's light at half alpha
		using GpuTexture target = GpuRig.Target(Device, 1, 1, format: TextureFormat.Rgba8UnormSrgb);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			CanvasParams p = CanvasParams.Primary(Viewport.Whole(new Vector2(1, 1)), CanvasLoadOp.Clear(SrgbStraight32.Black)) with {
				BlendMode = CanvasBlendMode.Opaque,
			};
			using (OwnedCanvas canvas = new(frame, p))
				canvas.Canvas.Texture(tex.Value, new RectF(0, 0, 1, 1));
			frame.Submit();
		}
		RawColor32 px = GpuRig.At(GpuRig.Read(Device, target), 1, 0, 0);
		Assert.InRange(px.R, 187, 189);
		Assert.Equal(128, px.A);
	}

	[Fact]
	public void MipLevelsComeFromTheMetadata() {
		using AssetStore store = new();
		MemorySource source = new();
		store.RegisterSource(new OwnerOrderedEntry<IAssetSource>(source, "test", "memory"));
		BuiltinAssetRegistrations.RegisterSrgbTexture2dInto(store, Device);
		using AssetThreadAttachment att = store.AttachCurrentThread();
		source.Set(new AssetId("test", "img.bmp"), bmp(8, 4));

		uint levels(string mips) {
			AssetId id = new("test", $"{Guid.NewGuid():N}.tex.json");
			source.Set(id, Encoding.UTF8.GetBytes($$"""{"Source":"test::img.bmp","Mips":{{mips}}}"""));
			return store.GetAsset<SrgbTexture2d>(id).Borrow().Value.MipLevelCount;
		}
		Assert.Equal(4u, levels("\"full\""));
		Assert.Equal(2u, levels("2"));
		Assert.IsType<InvalidDataException>(Assert.Throws<AssetLoadException>(() => levels("5")).InnerException);
		Assert.Throws<AssetLoadException>(() => levels("0"));
		Assert.Throws<AssetLoadException>(() => levels("\"half\""));
	}
}
