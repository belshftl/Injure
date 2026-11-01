// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.PixelConv;
using static Injure.Tests.Draw.PixelConv.Util;

namespace Injure.Tests.Draw.PixelConv;

public static class FullConversionTests {
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
		PixelFormatConvertOptions Options,
		int Width,
		int Height
	);

	public static readonly TheoryData<ConversionCase> Cases = new() {
		new ConversionCase(
			"Copy32SetAlpha_Rgba",
			ReferenceFamily.Copy32SetAlpha,
			PixelFormat.Rgba32Unorm,
			PixelFormat.Rgba32Unorm,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x1234, OverrideAlpha = true },
			257,
			7
		),
		new ConversionCase(
			"Copy32SetAlpha_Abgr",
			ReferenceFamily.Copy32SetAlpha,
			PixelFormat.Abgr32Unorm,
			PixelFormat.Abgr32Unorm,
			new PixelFormatConvertOptions { Alpha16Unorm = 0xbeef, OverrideAlpha = true },
			255,
			7
		),
		new ConversionCase(
			"Copy64SetAlpha_Rgba64_Le",
			ReferenceFamily.Copy64SetAlpha,
			PixelFormat.Rgba64UnormLe,
			PixelFormat.Rgba64UnormLe,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x2468, OverrideAlpha = true },
			129,
			5
		),
		new ConversionCase(
			"Copy64SetAlpha_Argb64_Be",
			ReferenceFamily.Copy64SetAlpha,
			PixelFormat.Argb64UnormBe,
			PixelFormat.Argb64UnormBe,
			new PixelFormatConvertOptions { Alpha16Unorm = 0xc39a, OverrideAlpha = true },
			131,
			5
		),
		new ConversionCase("Shuffle32_Rgba_to_Bgra", ReferenceFamily.Shuffle32, PixelFormat.Rgba32Unorm, PixelFormat.Bgra32Unorm, new PixelFormatConvertOptions(), 257, 7),
		new ConversionCase("Shuffle32_Argb_to_Abgr", ReferenceFamily.Shuffle32, PixelFormat.Argb32Unorm, PixelFormat.Abgr32Unorm, new PixelFormatConvertOptions(), 259, 5),
		new ConversionCase(
			"Expand24To32_Rgb_to_Bgra",
			ReferenceFamily.Expand24To32,
			PixelFormat.Rgb24Unorm,
			PixelFormat.Bgra32Unorm,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x5aa5 },
			263,
			6
		),
		new ConversionCase(
			"Expand24To32_Bgr_to_Argb",
			ReferenceFamily.Expand24To32,
			PixelFormat.Bgr24Unorm,
			PixelFormat.Argb32Unorm,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x1337 },
			261,
			6
		),
		new ConversionCase(
			"Contract32To24_Rgba_to_Rgb",
			ReferenceFamily.Contract32To24,
			PixelFormat.Rgba32Unorm,
			PixelFormat.Rgb24Unorm,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowDroppingAlpha },
			263,
			6
		),
		new ConversionCase(
			"Contract32To24_Abgr_to_Bgr",
			ReferenceFamily.Contract32To24,
			PixelFormat.Abgr32Unorm,
			PixelFormat.Bgr24Unorm,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowDroppingAlpha },
			259,
			6
		),
		new ConversionCase("Widen32To64_Bgra_to_Argb64_Le", ReferenceFamily.Widen32To64, PixelFormat.Bgra32Unorm, PixelFormat.Argb64UnormLe, new PixelFormatConvertOptions(), 173, 5),
		new ConversionCase("Widen32To64_Argb_to_Rgba64_Be", ReferenceFamily.Widen32To64, PixelFormat.Argb32Unorm, PixelFormat.Rgba64UnormBe, new PixelFormatConvertOptions(), 171, 5),
		new ConversionCase(
			"Narrow64To32_Rgba64_Le_to_Bgra",
			ReferenceFamily.Narrow64To32,
			PixelFormat.Rgba64UnormLe,
			PixelFormat.Bgra32Unorm,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing },
			173,
			5
		),
		new ConversionCase(
			"Narrow64To32_Abgr64_Be_to_Argb",
			ReferenceFamily.Narrow64To32,
			PixelFormat.Abgr64UnormBe,
			PixelFormat.Argb32Unorm,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing },
			169,
			5
		),
		new ConversionCase(
			"Packed16To32_Rgba4444_Le_to_Bgra",
			ReferenceFamily.Packed16To32,
			PixelFormat.Rgba4444UnormPack16Le,
			PixelFormat.Bgra32Unorm,
			new PixelFormatConvertOptions(),
			257,
			6
		),
		new ConversionCase(
			"Packed16To32_Rgb565_Be_to_Argb",
			ReferenceFamily.Packed16To32,
			PixelFormat.Rgb565UnormPack16Be,
			PixelFormat.Argb32Unorm,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x8181 },
			255,
			6
		),
		new ConversionCase(
			"Unpacked32ToPacked16_Bgra_to_Rgba4444_Be",
			ReferenceFamily.Unpacked32ToPacked16,
			PixelFormat.Bgra32Unorm,
			PixelFormat.Rgba4444UnormPack16Be,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing },
			257,
			6
		),
		new ConversionCase(
			"Unpacked32ToPacked16_Argb_to_Rgb565_Le",
			ReferenceFamily.Unpacked32ToPacked16,
			PixelFormat.Argb32Unorm,
			PixelFormat.Rgb565UnormPack16Le,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing | PixelFormatConversionFlags.AllowDroppingAlpha },
			255,
			6
		),
		new ConversionCase(
			"Generic_Rgba64_Le_to_Rgba5551_Be",
			ReferenceFamily.Generic,
			PixelFormat.Rgba64UnormLe,
			PixelFormat.Rgba5551UnormPack16Be,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing },
			37,
			3
		),
		new ConversionCase(
			"Generic_Rgba5551_Be_to_Rgba4444_Le",
			ReferenceFamily.Generic,
			PixelFormat.Rgba5551UnormPack16Be,
			PixelFormat.Rgba4444UnormPack16Le,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing },
			37,
			3
		),
		new ConversionCase(
			"Generic_Rgba4444_Le_SetAlpha",
			ReferenceFamily.Generic,
			PixelFormat.Rgba4444UnormPack16Le,
			PixelFormat.Rgba4444UnormPack16Le,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x9abc, OverrideAlpha = true },
			37,
			3
		),
		new ConversionCase(
			"Generic_Bgr24_to_Rgb565_Le",
			ReferenceFamily.Generic,
			PixelFormat.Bgr24Unorm,
			PixelFormat.Rgb565UnormPack16Le,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing },
			37,
			3
		),
		new ConversionCase(
			"Generic_Rgb565_Be_to_Abgr64_Le",
			ReferenceFamily.Generic,
			PixelFormat.Rgb565UnormPack16Be,
			PixelFormat.Abgr64UnormLe,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x4321 },
			37,
			3
		),
		new ConversionCase(
			"Generic_R8_to_Rgba64_Be",
			ReferenceFamily.Generic,
			PixelFormat.R8Unorm,
			PixelFormat.Rgba64UnormBe,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x0f0f },
			37,
			3
		),
		new ConversionCase("Generic_Rg16_to_Rgb24", ReferenceFamily.Generic, PixelFormat.Rg16Unorm, PixelFormat.Rgb24Unorm, new PixelFormatConvertOptions(), 37, 3),
		new ConversionCase("Generic_Rgb24_to_Bgr24", ReferenceFamily.Generic, PixelFormat.Rgb24Unorm, PixelFormat.Bgr24Unorm, new PixelFormatConvertOptions(), 37, 3),
		new ConversionCase("Generic_L8_to_Bgra32", ReferenceFamily.Generic, PixelFormat.L8Unorm, PixelFormat.Bgra32Unorm, new PixelFormatConvertOptions(), 37, 3),
		new ConversionCase("Generic_La16_to_Rgba64_Be", ReferenceFamily.Generic, PixelFormat.La16Unorm, PixelFormat.Rgba64UnormBe, new PixelFormatConvertOptions(), 37, 3),
		new ConversionCase(
			"Generic_La16_to_Rgb565_Le",
			ReferenceFamily.Generic,
			PixelFormat.La16Unorm,
			PixelFormat.Rgb565UnormPack16Le,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing | PixelFormatConversionFlags.AllowDroppingAlpha },
			37,
			3
		),
		new ConversionCase(
			"Generic_La16_SetAlpha",
			ReferenceFamily.Generic,
			PixelFormat.La16Unorm,
			PixelFormat.La16Unorm,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x7777, OverrideAlpha = true },
			37,
			3
		),
		new ConversionCase(
			"Generic_La16_to_L8",
			ReferenceFamily.Generic,
			PixelFormat.La16Unorm,
			PixelFormat.L8Unorm,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowDroppingAlpha },
			37,
			3
		),
		new ConversionCase("Generic_L8_to_R8", ReferenceFamily.Generic, PixelFormat.L8Unorm, PixelFormat.R8Unorm, new PixelFormatConvertOptions(), 37, 3),
		new ConversionCase(
			"Generic_R8_to_La16",
			ReferenceFamily.Generic,
			PixelFormat.R8Unorm,
			PixelFormat.La16Unorm,
			new PixelFormatConvertOptions { Alpha16Unorm = 0x3c3c },
			37,
			3
		),
		new ConversionCase(
			"Generic_Argb64_Be_to_Rg16",
			ReferenceFamily.Generic,
			PixelFormat.Argb64UnormBe,
			PixelFormat.Rg16Unorm,
			new PixelFormatConvertOptions { Flags = PixelFormatConversionFlags.AllowNarrowing | PixelFormatConversionFlags.AllowDroppingChannels },
			37,
			3
		),
	};

	[Theory]
	[MemberData(nameof(Cases))]
	public static void ConversionsMatchReference(ConversionCase c) {
		assertCaseMatchesReference(c, padSourceRows: false, padDestinationRows: false);
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public static void ConversionsMatchReferenceWithPaddedRows(ConversionCase c) {
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
			if (!PixelFormatConverter.TryCreatePlanWithBackend(c.SourceFormat, c.DestinationFormat, backend, c.Options, out PixelFormatConversionPlan plan))
				continue;
			sawAnything = true;
			Assert.Equal(backend, plan.Info.Backend);
			PlanExecutionPath path = c.Reference == ReferenceFamily.Generic ? PlanExecutionPath.GenericKernel : PlanExecutionPath.DedicatedKernel;
			Assert.Equal(path, plan.Info.ExecutionPath);

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
