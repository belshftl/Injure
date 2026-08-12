// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using Injure.Draw;
using Injure.Draw.PixelConv;
using Injure.Primitives;
using Injure.Rendering;
using StbImageSharp;

namespace Injure.Assets.Builtin;

public sealed class Texture2dSamplerModeJsonConverter : JsonStringEnumConverter<Texture2dSamplerMode> {
	public Texture2dSamplerModeJsonConverter() : base(namingPolicy: null, allowIntegerValues: false) {}
}

[JsonConverter(typeof(Texture2dSamplerModeJsonConverter))]
public enum Texture2dSamplerMode {
	NearestClamp,
	LinearClamp,
	NearestRepeat,
	LinearRepeat,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class Texture2dAssetMetadata {
	public required AssetId Source { get; init; }
	public RectI? SourceRect { get; init; } = null;
	public bool SRGB { get; init; } = true;
	public Texture2dSamplerMode SamplerMode { get; init; } = Texture2dSamplerMode.NearestClamp;
}

public sealed class Texture2dAssetData(
	Stream stream,
	Texture2dAssetMetadata metadata,
	string debugName,
	string? suggestedExtension = null,
	object? origin = null
) : AssetData(debugName, suggestedExtension, origin) {
	public readonly Stream Stream = stream;
	public readonly Texture2dAssetMetadata Metadata = metadata;
}

public sealed class Texture2dJsonAssetResolver : IAssetResolver {
	public async ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!info.AssetId.Path.EndsWith(".tex.json", StringComparison.Ordinal))
			return AssetResolveResult.NotHandled();
		await using Stream jsonStream = await info.FetchAsync(info.AssetId, ct).ConfigureAwait(false);
		Texture2dAssetMetadata meta;
		if (!mightBeJson(jsonStream))
			return AssetResolveResult.NotHandled();
		try {
			meta = await JsonSerializer.DeserializeAsync(jsonStream, InjureJsonContext.Default.Texture2dAssetMetadata, cancellationToken: ct).ConfigureAwait(false) ??
				throw new JsonException("expected nonnull json");
		} catch (JsonException) {
			return AssetResolveResult.NotHandled();
		}
		ct.ThrowIfCancellationRequested();
		Stream imgStream = await info.FetchAsync(meta.Source, ct).ConfigureAwait(false);
		return AssetResolveResult.Success(
			new Texture2dAssetData(
				imgStream,
				meta,
				meta.Source.ToString(),
				Path.GetExtension(meta.Source.Path),
				meta.Source
			)
		);
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
}

public sealed class Texture2dImageAssetResolver : IAssetResolver {
	public async ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		Stream imgStream = await info.FetchAsync(info.AssetId, ct).ConfigureAwait(false);
		Texture2dAssetMetadata meta = new() { Source = info.AssetId };
		ImageInfo? imageInfo = ImageInfo.FromStream(imgStream);
		if (imageInfo is null) {
			await imgStream.DisposeAsync();
			return AssetResolveResult.NotHandled();
		}
		imgStream.Position = 0;
		return AssetResolveResult.Success(
			new Texture2dAssetData(
				imgStream,
				meta,
				info.AssetId.ToString(),
				Path.GetExtension(info.AssetId.Path),
				info.AssetId
			)
		);
	}
}

public sealed class Texture2dAssetPreparedData(uint width, uint height, byte[] rgba, Texture2dAssetMetadata metadata) : AssetPreparedData {
	public readonly uint Width = width;
	public readonly uint Height = height;
	public readonly byte[] RGBA = rgba;
	public readonly Texture2dAssetMetadata Metadata = metadata;
}

public sealed class Texture2dAssetCreator(WebGpuDevice gpuDevice) : IAssetStagedCreator<Texture2d, Texture2dAssetPreparedData> {
	private readonly WebGpuDevice gpuDevice = gpuDevice;

	public async ValueTask<AssetPrepareResult<Texture2dAssetPreparedData>> TryPrepareAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (info.Data is not Texture2dAssetData data)
			return AssetPrepareResult<Texture2dAssetPreparedData>.NotHandled();
		await using Stream stream = data.Stream;
		var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
		if (image.Width <= 0 || image.Height <= 0)
			throw new AssetLoadException(info.AssetId, typeof(Texture2d), "image decode returned bogus dimensions");
		ct.ThrowIfCancellationRequested();
		return AssetPrepareResult<Texture2dAssetPreparedData>.Success(new Texture2dAssetPreparedData((uint)image.Width, (uint)image.Height, image.Data, data.Metadata));
	}

	public Texture2d Finalize(AssetFinalizeInfo<Texture2dAssetPreparedData> info) {
		Texture2dAssetPreparedData p = info.Prepared;
		Texture2dFormat fmt = p.Metadata.SRGB ? Texture2dFormat.Rgba32_Unorm_Srgb : Texture2dFormat.Rgba32_Unorm;
		GpuSamplerCreateParams smpParams = p.Metadata.SamplerMode switch {
			Texture2dSamplerMode.NearestClamp => SamplerStates.NearestClamp,
			Texture2dSamplerMode.LinearClamp => SamplerStates.LinearClamp,
			Texture2dSamplerMode.NearestRepeat => SamplerStates.NearestRepeat,
			Texture2dSamplerMode.LinearRepeat => SamplerStates.LinearRepeat,
			_ => throw new InvalidDataException($"invalid {nameof(Texture2dSamplerMode)} value '{p.Metadata.SamplerMode}'"),
		};
		ReadOnlySpan<byte> src;
		uint srcStride, w, h;
		if (p.Metadata.SourceRect is RectI r) {
			if (r.Width <= 0 || r.Height <= 0) throw new ArgumentException("texture source rect cannot have negative/zero dimensions");
			if (r.X < 0) throw new ArgumentException("texture source rect goes out of bounds (negative X)");
			if (r.Y < 0) throw new ArgumentException("texture source rect goes out of bounds (negative Y)");
			if (r.X + r.Width > p.Width) throw new ArgumentException("texture source rect goes out of bounds (X + width > texture width)");
			if (r.Y + r.Height > p.Height) throw new ArgumentException("texture source rect goes out of bounds (Y + height > texture height)");

			w = (uint)r.Width;
			h = (uint)r.Height;
			srcStride = p.Width * 4;
			src = p.RGBA.AsSpan(checked((int)(r.Y * (p.Width * 4) + r.X * 4)));
		} else {
			w = p.Width;
			h = p.Height;
			srcStride = p.Width * 4;
			src = p.RGBA;
		}

		Texture2d tex = new(gpuDevice, w, h, fmt);
		tex.Upload(src, checked((int)srcStride), PixelFormat.Rgba32_Unorm);
		return tex;
	}
}
