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

// a level count, or "full" for a full chain
public sealed class MipLevelsJsonConverter : JsonConverter<MipLevels> {
	public override MipLevels Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("full"u8))
			return MipLevels.FullChain;
		if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt32(out uint n) && n > 0)
			return MipLevels.Count(n);
		throw new JsonException("mip levels must be a positive level count or \"full\"");
	}

	public override void Write(Utf8JsonWriter writer, MipLevels value, JsonSerializerOptions options) {
		if (value.ExplicitCount is uint n)
			writer.WriteNumberValue(n);
		else
			writer.WriteStringValue("full");
	}
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SrgbTexture2dAssetMetadata {
	public required AssetId Source { get; init; }
	public RectI? SourceRect { get; init; } = null;
	public MipLevels Mips { get; init; } = MipLevels.Single;
}

public sealed class SrgbTexture2dAssetPreparedData(uint width, uint height, byte[] rgba, SrgbTexture2dAssetMetadata metadata) : AssetPreparedData {
	public readonly uint Width = width;
	public readonly uint Height = height;
	public readonly byte[] Rgba = rgba;
	public readonly SrgbTexture2dAssetMetadata Metadata = metadata;
}

public sealed class SrgbTexture2dAssetCreator(GpuDevice gpuDevice) : IAssetStagedCreator<SrgbTexture2d, SrgbTexture2dAssetPreparedData> {
	private readonly GpuDevice gpuDevice = gpuDevice;

	// handles both raw images and .tex.json metadata files pointing at one
	public async ValueTask<AssetPrepareResult<SrgbTexture2dAssetPreparedData>> TryPrepareAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		SrgbTexture2dAssetMetadata meta;
		Stream image;
		if (info.AssetId.Path.EndsWith(".tex.json", StringComparison.Ordinal)) {
			await using Stream json = await info.OpenAsync(ct).ConfigureAwait(false);
			if (!mightBeJson(json))
				return AssetPrepareResult<SrgbTexture2dAssetPreparedData>.NotHandled();
			meta = await JsonSerializer.DeserializeAsync(json, InjureJsonContext.Default.SrgbTexture2dAssetMetadata, cancellationToken: ct).ConfigureAwait(false) ??
				throw new InvalidDataException("texture metadata must not be null");
			image = await info.FetchAsync(meta.Source, ct).ConfigureAwait(false);
		} else {
			image = await info.OpenAsync(ct).ConfigureAwait(false);
			if (ImageInfo.FromStream(image) is null) {
				await image.DisposeAsync().ConfigureAwait(false);
				return AssetPrepareResult<SrgbTexture2dAssetPreparedData>.NotHandled();
			}
			image.Position = 0;
			meta = new SrgbTexture2dAssetMetadata { Source = info.AssetId };
		}

		ImageResult decoded;
		await using (image)
			decoded = ImageResult.FromStream(image, ColorComponents.RedGreenBlueAlpha);
		if (decoded.Width <= 0 || decoded.Height <= 0)
			throw new InvalidDataException("image decode returned bogus dimensions");
		// stb_image gives straight alpha, but SrgbTexture2d holds premultiplied color
		AlphaPremultiplier.Premultiply(decoded.Data, decoded.Width * 4, PixelFormat.Rgba32Unorm, decoded.Width, decoded.Height, TransferFunction.Srgb);
		if (meta.SourceRect is RectI r) {
			if (r.Width <= 0 || r.Height <= 0)
				throw new InvalidDataException("texture source rect cannot have negative/zero dimensions");
			if (r.X < 0 || r.Y < 0 || (long)r.X + r.Width > decoded.Width || (long)r.Y + r.Height > decoded.Height)
				throw new InvalidDataException($"texture source rect {r} goes out of bounds of the {decoded.Width}x{decoded.Height} image");
		}
		ct.ThrowIfCancellationRequested();
		return AssetPrepareResult<SrgbTexture2dAssetPreparedData>.Success(new SrgbTexture2dAssetPreparedData((uint)decoded.Width, (uint)decoded.Height, decoded.Data, meta));
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

	public SrgbTexture2d Finalize(AssetFinalizeInfo<SrgbTexture2dAssetPreparedData> info) {
		SrgbTexture2dAssetPreparedData p = info.Prepared;
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

		MipLevels mips = p.Metadata.Mips;
		if (!mips.IsFullChain && mips.ExplicitCount > MipLevels.FullChainLength(w, h))
			throw new InvalidDataException($"texture wants {mips.ExplicitCount} mip levels, but a full chain for its size of {w}x{h} only has {MipLevels.FullChainLength(w, h)}");
		// premultiplied in TryPrepareAsync
		SrgbTexture2d tex = new(gpuDevice, new SrgbTexture2dParams(w, h, TextureFormat.Rgba8UnormSrgb, mips));
		try {
			tex.Upload(src, srcStride);
		} catch {
			tex.Dispose();
			throw;
		}
		return tex;
	}
}
