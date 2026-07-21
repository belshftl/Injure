// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;
using Injure.Mods;
using Injure.Rendering;

namespace Injure.Assets.Builtin;

public static class BuiltinAssetRegistrations {
	public static void RegisterBaseInto(AssetStore store) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterDependencyWatcher(EngineInfo.OwnerId, new FileAssetDependencyWatcher(), "FileAssetDependencyWatcher");
	}

	public static void RegisterTexture2dInto(AssetStore store, WebGpuDevice gpuDevice) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterResolver(EngineInfo.OwnerId, new Texture2dJsonAssetResolver(), "Texture2dJsonAssetResolver");
		store.RegisterResolver(EngineInfo.OwnerId, new Texture2dImageAssetResolver(), "Texture2dImageAssetResolver");
		store.RegisterStagedCreator(EngineInfo.OwnerId, new Texture2dAssetCreator(gpuDevice), "Texture2dAssetCreator");
	}

	public static void RegisterFontInto(AssetStore store, TextSystem text) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterResolver(EngineInfo.OwnerId, new FontAssetResolver(), "FontSourceAssetResolver");
		store.RegisterCreator(EngineInfo.OwnerId, new FontAssetCreator(text), "FontSourceAssetCreator");
	}
}
