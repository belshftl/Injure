// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets.Builtin;

/// <summary>
/// An engine resource holding a built-in WGSL shader, and its entry points.
/// </summary>
/// <param name="ResourceId">The shader's engine resource.</param>
/// <param name="VsEntry">Name of the vertex shader entry point.</param>
/// <param name="FsEntry">Name of the fragment shader entry point.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct BuiltinShaderInfo(
	EngineResourceId ResourceId,
	string VsEntry,
	string FsEntry
);

/// <summary>
/// The shaders Injure's <see cref="Draw"/> types use, as engine resources embedded in the Injure
/// assembly.
/// </summary>
/// <remarks>
/// <see cref="Draw.CanvasSharedResources"/> loads them from its <see cref="EngineResourceStore"/>,
/// so that store needs a source serving them, e.g.
/// <c>new EmbeddedEngineResourceSource(typeof(Canvas).Assembly, [Primitive2d.ResourceId, ...])</c>
/// with all of them.
/// </remarks>
public static class BuiltinShaders {
	public static readonly BuiltinShaderInfo Primitive2d = new(
		ResourceId: new EngineResourceId("shaders/primitive2d.wgsl"),
		VsEntry: "vs_main",
		FsEntry: "fs_main"
	);

	public static readonly BuiltinShaderInfo Textured2dColor = new(
		ResourceId: new EngineResourceId("shaders/textured2dColor.wgsl"),
		VsEntry: "vs_main",
		FsEntry: "fs_main"
	);

	public static readonly BuiltinShaderInfo Textured2dRmask = new(
		ResourceId: new EngineResourceId("shaders/textured2dRMask.wgsl"),
		VsEntry: "vs_main",
		FsEntry: "fs_main"
	);

	public static readonly BuiltinShaderInfo Textured2dSdf = new(
		ResourceId: new EngineResourceId("shaders/textured2dSdf.wgsl"),
		VsEntry: "vs_main",
		FsEntry: "fs_main"
	);
}
