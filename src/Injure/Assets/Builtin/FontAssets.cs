// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;

namespace Injure.Assets.Builtin;

public sealed class FontAssetCreator(TextSystem text) : IAssetCreator<FontData> {
	private readonly TextSystem text = text;

	public async ValueTask<AssetCreateResult<FontData>> TryCreateAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		await using Stream stream = await info.OpenAsync(ct).ConfigureAwait(false);
		if (!looksLikeAFont(stream))
			return AssetCreateResult<FontData>.NotHandled();
		using MemoryStream ms = new();
		await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
		return AssetCreateResult<FontData>.Success(text.LoadFont(ms.ToArray(), info.AssetId.ToString()));
	}

	private static bool looksLikeAFont(Stream stream) {
		long saved = stream.Position;
		try {
			Span<byte> hdr = stackalloc byte[4];
			if (stream.ReadAtLeast(hdr, 4, throwOnEndOfStream: false) != 4)
				return false;
			return FontData.HasOpentypeHeader(hdr);
		} finally {
			stream.Position = saved;
		}
	}
}
