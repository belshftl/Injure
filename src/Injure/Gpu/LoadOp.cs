// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// What a render pass does with an attachment's contents when it starts.
/// </summary>
/// <remarks>
/// <para>
/// There is currently no <c>DontCare</c>, as <c>wgpu-native</c> doesn't have one.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </para>
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPULoadOp))]
public readonly partial struct LoadOp {
	/// <summary>Raw switch tag for <see cref="LoadOp"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; only valid for read-only depth/stencil aspects.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// Keeps the existing contents.
		/// </summary>
		Load = 1,

		/// <summary>
		/// Clears to the attachment's clear value.
		/// </summary>
		Clear = 2,
	}
}
