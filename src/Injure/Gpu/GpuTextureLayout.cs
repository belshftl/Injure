// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// How texel data is laid out in memory or in a GPU buffer.
/// </summary>
/// <param name="Offset">Byte offset to the start of the texel data.</param>
/// <param name="BytesBetweenRows">
/// <para>
/// Bytes from the start of one row to the start of the next (rows of blocks for block-compressed
/// formats). WebGPU calls this <c>bytesPerRow</c>. Must be at least the size of one row of the
/// region, and can be larger to skip padding between rows.
/// </para>
/// <para>
/// Copies on a <see cref="GpuCommandEncoder"/> require this to be a multiple of 256; queue writes
/// don't. Can be 0 for regions that are a single row, since there's no next row to get to.
/// </para>
/// </param>
/// <param name="RowsBetweenImages">
/// Rows (of blocks, for block-compressed formats) from the start of one image to the start of the
/// next, i.e. between depth slices or array layers. WebGPU calls this <c>rowsPerImage</c>. Must be
/// at least the region's height in rows. Can be 0 for regions that are a single image, since
/// there's no next image to get to.
/// </param>
/// <remarks>
/// <para>
/// Both are distances to the next row or image, so the last row only takes its own texels' worth
/// of bytes, and the last image only takes its own rows' worth. The data, in total, is
/// <c>Offset + BytesBetweenRows * RowsBetweenImages * (images - 1) + BytesBetweenRows * (rows - 1) + bytesInOneRow</c>
/// bytes, all of which a queue write reads, including any padding between rows, so they must all
/// be initialized.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly record struct GpuTextureLayout(
	ulong Offset,
	uint BytesBetweenRows,
	uint RowsBetweenImages
) : IHasInvalidDefault {
	/// <summary>
	/// <c>WGPU_COPY_STRIDE_UNDEFINED</c>.
	/// </summary>
	/// <remarks>
	/// wgpu uses this for an undefined stride rather than 0.
	/// </remarks>
	private const uint wgpuCopyStrideUndefined = uint.MaxValue;

	bool IHasInvalidDefault.IsDefault => this == default;

	internal uint WgpuBytesBetweenRows => BytesBetweenRows == 0 ? wgpuCopyStrideUndefined : BytesBetweenRows;
	internal uint WgpuRowsBetweenImages => RowsBetweenImages == 0 ? wgpuCopyStrideUndefined : RowsBetweenImages;
}
