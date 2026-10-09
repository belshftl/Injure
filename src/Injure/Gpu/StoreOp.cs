// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// What a render pass does with an attachment's contents when it ends.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUStoreOp))]
public readonly partial struct StoreOp {
	/// <summary>Raw switch tag for <see cref="StoreOp"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; only valid for read-only depth/stencil aspects.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// Keeps the rendered contents.
		/// </summary>
		Store = 1,

		/// <summary>
		/// Discards the rendered contents, leaving the attachment zeroed.
		/// </summary>
		Discard = 2,
	}
}
