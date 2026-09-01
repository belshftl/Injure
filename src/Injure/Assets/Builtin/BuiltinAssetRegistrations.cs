// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;
using Injure.Mods;
using Injure.Rendering;

namespace Injure.Assets.Builtin;

public static class BuiltinAssetRegistrations {
	public static void RegisterBaseInto(AssetStore store) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterDependencyWatcher(
			new OwnerOrderedEntry<IAssetDependencyWatcher<FileAssetDependency>>(
				new FileAssetDependencyWatcher(),
				EngineInfo.OwnerId,
				"FileAssetDependencyWatcher"
			)
		);
	}

	public static void RegisterTexture2dInto(AssetStore store, WebGpuDevice gpuDevice) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterResolver(
			new OwnerOrderedEntry<IAssetResolver>(
				new Texture2dJsonAssetResolver(),
				EngineInfo.OwnerId,
				"Texture2dJsonAssetResolver"
			)
		);
		store.RegisterResolver(
			new OwnerOrderedEntry<IAssetResolver>(
				new Texture2dImageAssetResolver(),
				EngineInfo.OwnerId,
				"Texture2dImageAssetResolver"
			)
		);
		store.RegisterStagedCreator(
			new OwnerOrderedEntry<IAssetStagedCreator<Draw.Texture2d, Texture2dAssetPreparedData>>(
				new Texture2dAssetCreator(gpuDevice),
				EngineInfo.OwnerId,
				"Texture2dAssetCreator"
			)
		);
	}

	public static void RegisterFontInto(AssetStore store, TextSystem text) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterResolver(
			new OwnerOrderedEntry<IAssetResolver>(
				new FontAssetResolver(),
				EngineInfo.OwnerId,
				"FontSourceAssetResolver"
			)
		);
		store.RegisterCreator(
			new OwnerOrderedEntry<IAssetCreator<Font>>(
				new FontAssetCreator(text),
				EngineInfo.OwnerId,
				"FontSourceAssetCreator"
			)
		);
	}
}
