// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using static Injure.Draw.PixelConv.ConverterCore;

namespace Injure.Draw.PixelConv;

using unsafe Kernel = delegate *<ref readonly PixelFormatConversionPlan, byte*, byte*, nuint, void>;

/// <summary>
/// Converts pixel data between <see cref="PixelFormat"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Channels are converted between bit depths by rounding to the nearest value, so for example 8
/// bits to 16 multiplies by 257. Channels that aren't in the source are filled in with 0 for R/G/B
/// and with <see cref="PixelFormatConvertOptions.Alpha16Unorm"/> for alpha.
/// </para>
/// <para>
/// Conversions that lose information (narrowing channels, or dropping alpha or color channels) are
/// refused unless a corresponding flag in <see cref="PixelFormatConversionFlags"/> to allow such a
/// lossy conversion is set.
/// </para>
/// </remarks>
public static unsafe class PixelFormatConverter {
	/// <summary>
	/// Prepares a conversion from <paramref name="srcFmt"/> to <paramref name="dstFmt"/>.
	/// </summary>
	/// <param name="srcFmt">Source pixel format.</param>
	/// <param name="dstFmt">Destination pixel format.</param>
	/// <param name="opts">Conversion options.</param>
	/// <returns>A plan that can be reused for any number of conversions.</returns>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="srcFmt"/> or <paramref name="dstFmt"/> is the invalid
	/// <see langword="default"/> value.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the conversion would narrow, drop alpha, or drop color channels without the
	/// corresponding <see cref="PixelFormatConversionFlags"/> in <paramref name="opts"/>, or if would
	/// convert a non-single-channel format to gray (see <see cref="PixelFormat"/>).
	/// </exception>
	public static PixelFormatConversionPlan CreatePlan(PixelFormat srcFmt, PixelFormat dstFmt, PixelFormatConvertOptions opts) {
		bool created = tryCreatePlan(srcFmt, dstFmt, opts, null, out PixelFormatConversionPlan plan);
		Debug.Assert(created);
		return plan;
	}

	/// <summary>
	/// Converts a 2D image; shorthand for
	/// <see cref="CreatePlan(PixelFormat, PixelFormat, PixelFormatConvertOptions)"/> followed by
	/// <see cref="PixelFormatConversionPlan.Convert(ReadOnlySpan{byte}, int, Span{byte}, int, int, int)"/>.
	/// </summary>
	/// <param name="src">Source image.</param>
	/// <param name="srcStride">Source row stride in bytes (not pixels).</param>
	/// <param name="srcFmt">Source pixel format.</param>
	/// <param name="dst">Destination buffer.</param>
	/// <param name="dstStride">Destination row stride in bytes (not pixels).</param>
	/// <param name="dstFmt">Destination pixel format.</param>
	/// <param name="width">Width of the image in pixels.</param>
	/// <param name="height">Height of the image in pixels.</param>
	/// <param name="opts">Conversion options.</param>
	/// <inheritdoc cref="CreatePlan(PixelFormat, PixelFormat, PixelFormatConvertOptions)" path="/exception"/>
	/// <inheritdoc cref="PixelFormatConversionPlan.Convert(ReadOnlySpan{byte}, int, Span{byte}, int, int, int)" path="/exception"/>
	/// <inheritdoc cref="PixelFormatConversionPlan.Convert(ReadOnlySpan{byte}, int, Span{byte}, int, int, int)" path="/remarks"/>
	public static void Convert(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		Span<byte> dst,
		int dstStride,
		PixelFormat dstFmt,
		int width,
		int height,
		PixelFormatConvertOptions opts
	) => CreatePlan(srcFmt, dstFmt, opts).Convert(src, srcStride, dst, dstStride, width, height);

	/// <summary>
	/// Converts a 2D image into a newly allocated, tightly packed buffer; shorthand for
	/// <see cref="CreatePlan(PixelFormat, PixelFormat, PixelFormatConvertOptions)"/> followed by
	/// <see cref="PixelFormatConversionPlan.Convert(ReadOnlySpan{byte}, int, int, int)"/>.
	/// </summary>
	/// <param name="src">Source image.</param>
	/// <param name="srcStride">Source row stride in bytes (not pixels).</param>
	/// <param name="srcFmt">Source pixel format.</param>
	/// <param name="dstFmt">Destination pixel format.</param>
	/// <param name="width">Width of the image in pixels.</param>
	/// <param name="height">Height of the image in pixels.</param>
	/// <param name="opts">Conversion options.</param>
	/// <param name="dstStride">
	/// Row stride of the returned buffer in bytes, i.e. <paramref name="width"/> multiplied by the
	/// destination's bytes per pixel.
	/// </param>
	/// <returns>A newly allocated buffer with the converted image.</returns>
	/// <inheritdoc cref="CreatePlan(PixelFormat, PixelFormat, PixelFormatConvertOptions)" path="/exception"/>
	/// <inheritdoc cref="PixelFormatConversionPlan.Convert(ReadOnlySpan{byte}, int, int, int)" path="/exception"/>
	public static byte[] Convert(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		PixelFormatConvertOptions opts,
		out int dstStride
	) {
		PixelFormatConversionPlan plan = CreatePlan(srcFmt, dstFmt, opts);
		dstStride = width * plan.DestinationBytesPerPixel;
		return plan.Convert(src, srcStride, width, height);
	}

	/// <returns>
	/// <see langword="false"/> if <paramref name="selected"/> isn't supported on this machine or has
	/// no kernel for the conversion.
	/// </returns>
	internal static bool TryCreatePlanWithBackend(
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		PlanBackend selected,
		PixelFormatConvertOptions opts,
		out PixelFormatConversionPlan plan
	) => tryCreatePlan(srcFmt, dstFmt, opts, selected, out plan);

	/// <remarks>
	/// Picks the best backend if <paramref name="selected"/> is <see langword="null"/>, which also
	/// never returns <see langword="false"/>.
	/// </remarks>
	private static bool tryCreatePlan(
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		PixelFormatConvertOptions opts,
		PlanBackend? selected,
		out PixelFormatConversionPlan plan
	) {
		plan = default;
		ArgumentInvalidDefaultException.ThrowIfDefault(srcFmt);
		ArgumentInvalidDefaultException.ThrowIfDefault(dstFmt);
		PixelFormatDesc srcDesc = DescOf(srcFmt);
		PixelFormatDesc dstDesc = DescOf(dstFmt);

		if (IsUnsupportedColorToGray(in srcDesc, in dstDesc))
			throw new ArgumentException("converting color to gray isn't supported", nameof(dstFmt));
		if (WouldNarrow(in srcDesc, in dstDesc) && opts.Flags.HasNone(PixelFormatConversionFlags.AllowNarrowing))
			throw new ArgumentException("conversion would narrow; refusing to convert without PixelFormatConversionFlags.AllowNarrowing", nameof(opts));
		if (WouldDropAlpha(in srcDesc, in dstDesc) && opts.Flags.HasNone(PixelFormatConversionFlags.AllowDroppingAlpha))
			throw new ArgumentException("conversion would drop alpha; refusing to convert without PixelFormatConversionFlags.AllowDroppingAlpha", nameof(opts));
		if (WouldDropColorChannels(in srcDesc, in dstDesc) && opts.Flags.HasNone(PixelFormatConversionFlags.AllowDroppingColorChannels))
			throw new ArgumentException("conversion would drop one or more color channels; refusing to convert without PixelFormatConversionFlags.AllowDroppingColorChannels", nameof(opts));

		PlanKind kind = Classify(srcFmt, dstFmt, in srcDesc, in dstDesc, in opts);
		PayloadUnion payload = MakePayload(kind, in srcDesc, in dstDesc, in opts);
		Kernel kernel = null;
		PlanInfo info;
		if (kind == PlanKind.Memcpy) {
			if (selected is not null && selected != PlanBackend.None)
				return false;
			info = new PlanInfo(PlanExecutionPath.Memcpy, PlanBackend.None);
		} else {
			PlanBackend backend;
			if (selected is null) {
				kernel = KernelRegistry.Kernels[(int)kind].Pick(out backend);
			} else {
				backend = selected.Value;
				if (!KernelRegistry.Kernels[(int)kind].TryPickSpecific(backend, out kernel))
					return false;
			}
			PlanExecutionPath path = kind != PlanKind.Generic ? PlanExecutionPath.DedicatedKernel : PlanExecutionPath.GenericKernel;
			info = new PlanInfo(path, backend);
		}
		plan = new PixelFormatConversionPlan(srcFmt, dstFmt, opts, kind, info, kernel, srcDesc, dstDesc, payload);
		return true;
	}
}
