// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Which triangles to discard based on their facing.
/// </summary>
/// <remarks>
/// <para>
/// Facing is determined by <see cref="PrimitiveState.FrontFace"/>; see <see cref="FrontFace"/>.
/// Points and lines are never culled.
/// </para>
/// <para>
/// Degenerate triangles are back-facing under both <see cref="FrontFace.Ccw"/> and
/// <see cref="FrontFace.Cw"/>, so only <see cref="Back"/> discards them. Under <see cref="None"/>
/// and <see cref="Front"/>, they enclose no pixel centers, but whether a pixel center exactly on
/// one produces a fragment is undefined.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUCullMode), Subset = true)]
public readonly partial struct CullMode {
	/// <summary>Raw switch tag for <see cref="CullMode"/>.</summary>
	public enum Case {
		/// <summary>Discards nothing.</summary>
		None = 1,

		/// <summary>
		/// Discards front-facing triangles.
		/// </summary>
		Front = 2,

		/// <summary>
		/// Discards back-facing triangles, including degenerate ones (see type-level docs).
		/// </summary>
		Back = 3,
	}
}
