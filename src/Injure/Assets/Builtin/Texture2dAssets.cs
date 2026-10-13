// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using Injure.Draw;
using Injure.Draw.PixelConv;
using Injure.Primitives;
using Injure.Gpu;
using StbImageSharp;

namespace Injure.Assets.Builtin;

public sealed class Texture2dAssetMipsJsonConverter : JsonStringEnumConverter<Texture2dAssetMips> {
	public Texture2dAssetMipsJsonConverter() : base(namingPolicy: null, allowIntegerValues: false) {}
}

// mirrors Texture2dCreateParams.Mips, as a plain enum for JSON
[JsonConverter(typeof(Texture2dAssetMipsJsonConverter))]
public enum Texture2dAssetMips {
	None,
	Color,
	Raw,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class Texture2dAssetMetadata {
	public required AssetId Source { get; init; }
	public RectI? SourceRect { get; init; } = null;
	public Texture2dAssetMips Mips { get; init; } = Texture2dAssetMips.None;
}

public sealed class Texture2dAssetPreparedData(uint width, uint height, byte[] rgba, Texture2dAssetMetadata metadata) : AssetPreparedData {
	public readonly uint Width = width;
	public readonly uint Height = height;
	public readonly byte[] Rgba = rgba;
	public readonly Texture2dAssetMetadata Metadata = metadata;
}

public sealed class Texture2dAssetCreator(GpuDevice gpuDevice) : IAssetStagedCreator<Texture2d, Texture2dAssetPreparedData> {
	private readonly GpuDevice gpuDevice = gpuDevice;

	// handles both raw images and .tex.json metadata files pointing at one
	public async ValueTask<AssetPrepareResult<Texture2dAssetPreparedData>> TryPrepareAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		Texture2dAssetMetadata meta;
		Stream image;
		if (info.AssetId.Path.EndsWith(".tex.json", StringComparison.Ordinal)) {
			await using Stream json = await info.OpenAsync(ct).ConfigureAwait(false);
			if (!mightBeJson(json))
				return AssetPrepareResult<Texture2dAssetPreparedData>.NotHandled();
			meta = await JsonSerializer.DeserializeAsync(json, InjureJsonContext.Default.Texture2dAssetMetadata, cancellationToken: ct).ConfigureAwait(false) ??
				throw new InvalidDataException("texture metadata must not be null");
			image = await info.FetchAsync(meta.Source, ct).ConfigureAwait(false);
		} else {
			image = await info.OpenAsync(ct).ConfigureAwait(false);
			if (ImageInfo.FromStream(image) is null) {
				await image.DisposeAsync().ConfigureAwait(false);
				return AssetPrepareResult<Texture2dAssetPreparedData>.NotHandled();
			}
			image.Position = 0;
			meta = new Texture2dAssetMetadata { Source = info.AssetId };
		}

		ImageResult decoded;
		await using (image)
			decoded = ImageResult.FromStream(image, ColorComponents.RedGreenBlueAlpha);
		if (decoded.Width <= 0 || decoded.Height <= 0)
			throw new InvalidDataException("image decode returned bogus dimensions");
		if (meta.SourceRect is RectI r) {
			if (r.Width <= 0 || r.Height <= 0)
				throw new InvalidDataException("texture source rect cannot have negative/zero dimensions");
			if (r.X < 0 || r.Y < 0 || (long)r.X + r.Width > decoded.Width || (long)r.Y + r.Height > decoded.Height)
				throw new InvalidDataException($"texture source rect {r} goes out of bounds of the {decoded.Width}x{decoded.Height} image");
		}
		ct.ThrowIfCancellationRequested();
		return AssetPrepareResult<Texture2dAssetPreparedData>.Success(new Texture2dAssetPreparedData((uint)decoded.Width, (uint)decoded.Height, decoded.Data, meta));
	}

	private static bool mightBeJson(Stream stream) {
		long saved = stream.Position;
		try {
			int b;
			do {
				b = stream.ReadByte();
				if (b < 0)
					return false;
			} while (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n');
			return b is (byte)'{' or (byte)'[' or (byte)'"' or (byte)'t' or (byte)'f' or (byte)'n' or (byte)'-' ||
				b >= (byte)'0' && b <= (byte)'9';
		} finally {
			stream.Position = saved;
		}
	}

	public Texture2d Finalize(AssetFinalizeInfo<Texture2dAssetPreparedData> info) {
		Texture2dAssetPreparedData p = info.Prepared;
		Texture2dFormat fmt = Texture2dFormat.Rgba32Unorm;
		ReadOnlySpan<byte> src;
		uint srcStride, w, h;
		// the source rect was validated in TryPrepareAsync
		if (p.Metadata.SourceRect is RectI r) {
			w = (uint)r.Width;
			h = (uint)r.Height;
			srcStride = p.Width * 4;
			src = p.Rgba.AsSpan(checked((int)(r.Y * (p.Width * 4) + r.X * 4)));
		} else {
			w = p.Width;
			h = p.Height;
			srcStride = p.Width * 4;
			src = p.Rgba;
		}

		Texture2dMipFilter? mips = p.Metadata.Mips switch {
			Texture2dAssetMips.None => null,
			Texture2dAssetMips.Color => Texture2dMipFilter.Color,
			Texture2dAssetMips.Raw => Texture2dMipFilter.Raw,
			_ => throw new InvalidDataException($"invalid {nameof(Texture2dAssetMips)} value '{p.Metadata.Mips}'"),
		};
		Texture2d tex = new(gpuDevice, new Texture2dCreateParams(w, h, fmt, mips));
		tex.Upload(src, checked((int)srcStride), PixelFormat.Rgba32_Unorm);
		return tex;
	}
}
