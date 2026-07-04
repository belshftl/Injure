// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets.Builtin;

public readonly record struct BuiltinShaderInfo(
	EngineResourceId ResourceId,
	string VsEntry,
	string FsEntry
);

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
		ResourceId: new EngineResourceId("shaders/textured2dSDF.wgsl"),
		VsEntry: "vs_main",
		FsEntry: "fs_main"
	);
}
