// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Gpu;

/// <summary>
/// Fragment stage state for a render pipeline.
/// </summary>
/// <param name="ShaderModule">Shader module containing the fragment entry point.</param>
/// <param name="EntryPoint">Name of the <c>@fragment</c> function in the module.</param>
/// <param name="Targets">
/// Color targets, one per color attachment of the render passes the pipeline is used in, in the
/// same order; target <c>i</c> receives the shader output at <c>@location(i)</c>. Must not be
/// empty.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct FragmentState(
	GpuShaderModuleHandle ShaderModule,
	string EntryPoint,
	ImmutableArray<ColorTargetState> Targets
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
