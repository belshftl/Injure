// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// What a <see cref="GpuTexture"/> may be used for.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedFlags]
[ClosedFlagsMirror(typeof(WGPUTextureUsage))]
public readonly partial struct TextureUsage {
	/// <summary>Raw bits for <see cref="TextureUsage"/>.</summary>
	[Flags]
	public enum Bits : ulong {
		/// <summary>No usages.</summary>
		None = 0ul,

		/// <summary>
		/// Can be the source of copies.
		/// </summary>
		CopySrc = 1ul,

		/// <summary>
		/// Can be the destination of copies and queue writes.
		/// </summary>
		CopyDst = 2ul,

		/// <summary>
		/// Can be bound as a sampled texture.
		/// </summary>
		TextureBinding = 4ul,

		/// <summary>
		/// Can be bound as a storage texture.
		/// </summary>
		StorageBinding = 8ul,

		/// <summary>
		/// Can be a render pass attachment.
		/// </summary>
		RenderAttachment = 0x10ul,
	}
}
