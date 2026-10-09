// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// What a <see cref="GpuBuffer"/> may be used for.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedFlags]
[ClosedFlagsMirror(typeof(WGPUBufferUsage))]
public readonly partial struct BufferUsage {
	/// <summary>Raw bits for <see cref="BufferUsage"/>.</summary>
	[Flags]
	public enum Bits : ulong {
		/// <summary>No usages.</summary>
		None = 0ul,

		/// <summary>
		/// Can be mapped for reading; only combinable with <see cref="CopyDst"/>.
		/// </summary>
		MapRead = 1ul,

		/// <summary>
		/// Can be mapped for writing; only combinable with <see cref="CopySrc"/>.
		/// </summary>
		MapWrite = 2ul,

		/// <summary>
		/// Can be the source of copies.
		/// </summary>
		CopySrc = 4ul,

		/// <summary>
		/// Can be the destination of copies and queue writes.
		/// </summary>
		CopyDst = 8ul,

		/// <summary>
		/// Can be used as an index buffer.
		/// </summary>
		Index = 0x10ul,

		/// <summary>
		/// Can be used as a vertex buffer.
		/// </summary>
		Vertex = 0x20ul,

		/// <summary>
		/// Can be bound as a uniform buffer.
		/// </summary>
		Uniform = 0x40ul,

		/// <summary>
		/// Can be bound as a storage buffer.
		/// </summary>
		Storage = 0x80ul,

		/// <summary>
		/// Can hold indirect draw arguments.
		/// </summary>
		Indirect = 0x100ul,

		/// <summary>
		/// Can be the destination of query resolves; queries aren't supported yet.
		/// </summary>
		QueryResolve = 0x200ul,
	}
}
