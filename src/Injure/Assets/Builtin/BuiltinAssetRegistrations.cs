// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;
using Injure.Mods;
using Injure.Gpu;

namespace Injure.Assets.Builtin;

public static class BuiltinAssetRegistrations {
	public static void RegisterBaseInto(AssetStore store) {
		ArgumentNullException.ThrowIfNull(store);
		store.RegisterDepWatcher(
			new OwnerOrderedEntry<IAssetDepWatcher<FileAssetDep>>(
				new FileAssetDepWatcher(),
				EngineInfo.OwnerId,
				"FileAssetDepWatcher"
			)
		);
	}

	public static void RegisterTexture2dInto(AssetStore store, GpuDevice gpuDevice) {
		ArgumentNullException.ThrowIfNull(store);
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
		store.RegisterCreator(
			new OwnerOrderedEntry<IAssetCreator<FontData>>(
				new FontAssetCreator(text),
				EngineInfo.OwnerId,
				"FontAssetCreator"
			)
		);
	}
}
