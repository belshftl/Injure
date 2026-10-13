// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;

namespace Injure.Assets.Builtin;

public sealed class FontAssetCreator(TextSystem text) : IAssetCreator<Font> {
	private readonly TextSystem text = text;

	public async ValueTask<AssetCreateResult<Font>> TryCreateAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		await using Stream stream = await info.OpenAsync(ct).ConfigureAwait(false);
		if (!looksLikeAFont(stream))
			return AssetCreateResult<Font>.NotHandled();
		using MemoryStream ms = new();
		await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
		return AssetCreateResult<Font>.Success(text.LoadFont(ms.ToArray(), info.AssetId.ToString()));
	}

	private static bool looksLikeAFont(Stream stream) {
		long saved = stream.Position;
		try {
			Span<byte> hdr = stackalloc byte[4];
			if (stream.ReadAtLeast(hdr, 4, throwOnEndOfStream: false) != 4)
				return false;
			return ((uint)hdr[0] << 24 | (uint)hdr[1] << 16 | (uint)hdr[2] << 8 | hdr[3]) is
				0x00010000 or // truetype / opentype with truetype outlines
				0x4f54544f or // 'OTTO' (opentype with cff/cff2 outlines)
				0x74746366 or // 'ttcf' (truetype collection)
				0x774f4646 or // 'wOFF' (woff 1)
				0x774f4632 or // 'wOF2' (woff 2)
				0x74727565; // 'true' (old apple tag for sfnt-wrapped truetype)
		} finally {
			stream.Position = saved;
		}
	}
}
