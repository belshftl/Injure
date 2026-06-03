// SPDX-License-Identifier: MIT

namespace Injure.ModKit.Analyzers.Discouraged;

internal static class KnownTypeMetadataNames {
	public const string Hook = "MonoMod.RuntimeDetour.Hook";
	public const string ILHook = "MonoMod.RuntimeDetour.ILHook";
	public const string NativeHook = "MonoMod.RuntimeDetour.NativeHook";
	public const string DetourConfig = "MonoMod.RuntimeDetour.DetourConfig";
	public const string ILCursor = "MonoMod.Cil.ILCursor";

	public const string IModLoadContext = "Injure.ModKit.Abstractions.IModLoadContext`2";
	public const string IModLinkContext = "Injure.ModKit.Abstractions.IModLinkContext`2";
	public const string IModActivateContext = "Injure.ModKit.Abstractions.IModActivateContext`2";
	public const string IModReloadContext = "Injure.ModKit.Abstractions.IModReloadContext`2";
}
