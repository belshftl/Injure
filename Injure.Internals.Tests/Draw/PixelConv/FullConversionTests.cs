// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.PixelConv;
using static Injure.Internals.Tests.Draw.PixelConv.Util;

namespace Injure.Internals.Tests.Draw.PixelConv;

public sealed class FullConversionTests {
	private const byte srcGuard = 0xc7;
	private const byte dstGuard = 0x97;
	private const int prefixPad = 11;
	private const int suffixPad = 19;

	private static readonly PlanBackend[] backends = [
		PlanBackend.Avx2,
		PlanBackend.Ssse3,
		PlanBackend.Sse2,
		PlanBackend.AdvSimd,
		PlanBackend.Scalar,
	];

	public readonly record struct ConversionCase(
		string Name,
		ReferenceFamily Reference,
		PixelFormat SourceFormat,
		PixelFormat DestinationFormat,
		PixelConvertOptions Options,
		int Width,
		int Height
	);

	public static readonly TheoryData<ConversionCase> Cases = new() {
		new ConversionCase(
			"Copy32SetAlpha_Rgba",
			ReferenceFamily.Copy32SetAlpha,
			PixelFormat.Rgba32_Unorm,
			PixelFormat.Rgba32_Unorm,
			new PixelConvertOptions { Alpha16Unorm = 0x1234, OverrideAlpha = true },
			257,
			7
		),
		new ConversionCase(
			"Copy32SetAlpha_Abgr",
			ReferenceFamily.Copy32SetAlpha,
			PixelFormat.Abgr32_Unorm,
			PixelFormat.Abgr32_Unorm,
			new PixelConvertOptions { Alpha16Unorm = 0xbeef, OverrideAlpha = true },
			255,
			7
		),
		new ConversionCase(
			"Copy64SetAlpha_Rgba64_Le",
			ReferenceFamily.Copy64SetAlpha,
			PixelFormat.Rgba64_Unorm_Le,
			PixelFormat.Rgba64_Unorm_Le,
			new PixelConvertOptions { Alpha16Unorm = 0x2468, OverrideAlpha = true },
			129,
			5
		),
		new ConversionCase(
			"Copy64SetAlpha_Argb64_Be",
			ReferenceFamily.Copy64SetAlpha,
			PixelFormat.Argb64_Unorm_Be,
			PixelFormat.Argb64_Unorm_Be,
			new PixelConvertOptions { Alpha16Unorm = 0xc39a, OverrideAlpha = true },
			131,
			5
		),
		new ConversionCase("Shuffle32_Rgba_to_Bgra", ReferenceFamily.Shuffle32, PixelFormat.Rgba32_Unorm, PixelFormat.Bgra32_Unorm, new PixelConvertOptions(), 257, 7),
		new ConversionCase("Shuffle32_Argb_to_Abgr", ReferenceFamily.Shuffle32, PixelFormat.Argb32_Unorm, PixelFormat.Abgr32_Unorm, new PixelConvertOptions(), 259, 5),
		new ConversionCase(
			"Expand24To32_Rgb_to_Bgra",
			ReferenceFamily.Expand24To32,
			PixelFormat.Rgb24_Unorm,
			PixelFormat.Bgra32_Unorm,
			new PixelConvertOptions { Alpha16Unorm = 0x5aa5 },
			263,
			6
		),
		new ConversionCase(
			"Expand24To32_Bgr_to_Argb",
			ReferenceFamily.Expand24To32,
			PixelFormat.Bgr24_Unorm,
			PixelFormat.Argb32_Unorm,
			new PixelConvertOptions { Alpha16Unorm = 0x1337 },
			261,
			6
		),
		new ConversionCase(
			"Contract32To24_Rgba_to_Rgb",
			ReferenceFamily.Contract32To24,
			PixelFormat.Rgba32_Unorm,
			PixelFormat.Rgb24_Unorm,
			new PixelConvertOptions { Flags = ConversionFlags.AllowDroppingAlpha },
			263,
			6
		),
		new ConversionCase(
			"Contract32To24_Abgr_to_Bgr",
			ReferenceFamily.Contract32To24,
			PixelFormat.Abgr32_Unorm,
			PixelFormat.Bgr24_Unorm,
			new PixelConvertOptions { Flags = ConversionFlags.AllowDroppingAlpha },
			259,
			6
		),
		new ConversionCase("Widen32To64_Bgra_to_Argb64_Le", ReferenceFamily.Widen32To64, PixelFormat.Bgra32_Unorm, PixelFormat.Argb64_Unorm_Le, new PixelConvertOptions(), 173, 5),
		new ConversionCase("Widen32To64_Argb_to_Rgba64_Be", ReferenceFamily.Widen32To64, PixelFormat.Argb32_Unorm, PixelFormat.Rgba64_Unorm_Be, new PixelConvertOptions(), 171, 5),
		new ConversionCase(
			"Narrow64To32_Rgba64_Le_to_Bgra",
			ReferenceFamily.Narrow64To32,
			PixelFormat.Rgba64_Unorm_Le,
			PixelFormat.Bgra32_Unorm,
			new PixelConvertOptions { Flags = ConversionFlags.AllowNarrowing },
			173,
			5
		),
		new ConversionCase(
			"Narrow64To32_Abgr64_Be_to_Argb",
			ReferenceFamily.Narrow64To32,
			PixelFormat.Abgr64_Unorm_Be,
			PixelFormat.Argb32_Unorm,
			new PixelConvertOptions { Flags = ConversionFlags.AllowNarrowing },
			169,
			5
		),
		new ConversionCase(
			"Packed16To32_Rgba4444_Le_to_Bgra",
			ReferenceFamily.Packed16To32,
			PixelFormat.Rgba4444_UnormPack16_Le,
			PixelFormat.Bgra32_Unorm,
			new PixelConvertOptions(),
			257,
			6
		),
		new ConversionCase(
			"Packed16To32_Bgr565_Be_to_Argb",
			ReferenceFamily.Packed16To32,
			PixelFormat.Bgr565_UnormPack16_Be,
			PixelFormat.Argb32_Unorm,
			new PixelConvertOptions { Alpha16Unorm = 0x8181 },
			255,
			6
		),
		new ConversionCase(
			"Unpacked32ToPacked16_Bgra_to_Rgba4444_Be",
			ReferenceFamily.Unpacked32ToPacked16,
			PixelFormat.Bgra32_Unorm,
			PixelFormat.Rgba4444_UnormPack16_Be,
			new PixelConvertOptions { Flags = ConversionFlags.AllowNarrowing },
			257,
			6
		),
		new ConversionCase(
			"Unpacked32ToPacked16_Argb_to_Bgr565_Le",
			ReferenceFamily.Unpacked32ToPacked16,
			PixelFormat.Argb32_Unorm,
			PixelFormat.Bgr565_UnormPack16_Le,
			new PixelConvertOptions { Flags = ConversionFlags.AllowNarrowing | ConversionFlags.AllowDroppingAlpha },
			255,
			6
		),
	};

	[Theory]
	[MemberData(nameof(Cases))]
	public void ConversionsMatchReference(ConversionCase c) {
		assertCaseMatchesReference(c, padSourceRows: false, padDestinationRows: false);
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public void ConversionsMatchReferenceWithPaddedRows(ConversionCase c) {
		assertCaseMatchesReference(c, padSourceRows: true, padDestinationRows: true);
	}

	private static void assertCaseMatchesReference(ConversionCase c, bool padSourceRows, bool padDestinationRows) {
		int srcRowBytes = checked(c.Width * GetBytesPerPixel(c.SourceFormat));
		int dstRowBytes = checked(c.Width * GetBytesPerPixel(c.DestinationFormat));
		int srcStride = srcRowBytes + (padSourceRows ? 13 : 0);
		int dstStride = dstRowBytes + (padDestinationRows ? 29 : 0);

		int srcBytes = checked(srcStride * c.Height);
		int dstBytes = checked(dstStride * c.Height);
		byte[] srcBuf = new byte[prefixPad + srcBytes + suffixPad];
		byte[] dstBuf = new byte[prefixPad + dstBytes + suffixPad];
		srcBuf.AsSpan().Fill(srcGuard);
		dstBuf.AsSpan().Fill(dstGuard);
		patfill(srcBuf.AsSpan(prefixPad, srcBytes), fnv(c.Name));

		ReadOnlySpan<byte> src = srcBuf.AsSpan(prefixPad, srcBytes);
		byte[] reference = ReferenceConverter.Convert(c.Reference, src, srcStride, c.SourceFormat, c.DestinationFormat, c.Width, c.Height, c.Options);
		int referenceStride = dstRowBytes;

		bool sawAnything = false;
		foreach (PlanBackend backend in backends) {
			if (!PixelConverter.TryCreatePlanWithBackend(c.SourceFormat, c.DestinationFormat, backend, out PixelConversionPlan plan, c.Options))
				continue;
			sawAnything = true;
			Assert.Equal(backend, plan.Info.Backend);
			Assert.Equal(PlanExecutionPath.DedicatedKernel, plan.Info.ExecutionPath);

			dstBuf.AsSpan().Fill(dstGuard);
			Span<byte> result = dstBuf.AsSpan(prefixPad, dstBytes);
			plan.Convert(src, srcStride, result, dstStride, c.Width, c.Height);
			assertPxEqual(reference, result, referenceStride, dstStride, dstRowBytes, c.Height, c.Name, backend);
			assertGuards(srcBuf, srcGuard, c.Name, backend, "src");
			assertGuards(dstBuf, dstGuard, c.Name, backend, "dst");
		}
		Assert.True(sawAnything, $"couldn't create any plans for case '{c.Name}'");
	}

	private static void assertPxEqual(
		ReadOnlySpan<byte> a,
		ReadOnlySpan<byte> b,
		int aStride,
		int bStride,
		int rowBytes,
		int height,
		string caseName,
		PlanBackend backend
	) {
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> aRow = a.Slice(y * aStride, rowBytes);
			ReadOnlySpan<byte> bRow = b.Slice(y * bStride, rowBytes);
			Assert.True(aRow.SequenceEqual(bRow), $"pixel mismatch in case '{caseName}' backend {backend} row {y}");
		}
	}

	private static void assertGuards(byte[] buf, byte expected, string caseName, PlanBackend backend, string which) {
		for (int i = 0; i < prefixPad; i++)
			Assert.True(buf[i] == expected, $"{which} prefix guard corrupted in case '{caseName}' backend {backend} index {i}");
		for (int i = buf.Length - suffixPad; i < buf.Length; i++)
			Assert.True(buf[i] == expected, $"{which} suffix guard corrupted in case '{caseName}' backend {backend} index {i}");
	}

	private static void patfill(Span<byte> buf, uint seed) {
		uint s = seed;
		for (int i = 0; i < buf.Length; i++) {
			s ^= s << 13;
			s ^= s >> 17;
			s ^= s << 5;
			buf[i] = (byte)s;
		}
	}

	private static uint fnv(string s) {
		uint h = 2166136261u;
		for (int i = 0; i < s.Length; i++) {
			h ^= s[i];
			h *= 16777619u;
		}
		return h;
	}
}
