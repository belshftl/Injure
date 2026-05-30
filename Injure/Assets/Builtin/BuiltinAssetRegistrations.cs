// SPDX-License-Identifier: MIT

using System;

using Injure.Graphics.Text;
using Injure.ModKit.Abstractions;
using Injure.Rendering;

namespace Injure.Assets.Builtin;

public static class BuiltinAssetRegistrations {
	public static void RegisterBaseInto(AssetStore store) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterDependencyWatcher(EngineInfo.OwnerID, new FileAssetDependencyWatcher(), "FileAssetDependencyWatcher");
	}

	public static void RegisterTexture2DInto(AssetStore store, WebGPUDevice gpuDevice) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterResolver(EngineInfo.OwnerID, new Texture2DJsonAssetResolver(), "Texture2DJsonAssetResolver");
		store.RegisterResolver(EngineInfo.OwnerID, new Texture2DImageAssetResolver(), "Texture2DImageAssetResolver");
		store.RegisterStagedCreator(EngineInfo.OwnerID, new Texture2DAssetCreator(gpuDevice), "Texture2DAssetCreator");
	}

	public static void RegisterFontInto(AssetStore store, TextSystem text) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterResolver(EngineInfo.OwnerID, new FontAssetResolver(), "FontSourceAssetResolver");
		store.RegisterCreator(EngineInfo.OwnerID, new FontAssetCreator(text), "FontSourceAssetCreator");
	}
}
