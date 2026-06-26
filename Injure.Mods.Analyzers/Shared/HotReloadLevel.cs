// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Shared;

internal enum ModAssemblyHotReloadLevelMirror {
	None = 1,
	SafeBoundary = 2,
	Live = 3,
}

internal static class HotReloadModel {
	public static bool TryGetHotReloadLevel(Compilation compilation, out ModAssemblyHotReloadLevelMirror lv) {
		var m = Core.Model.Create(compilation);
		if (m.ModAssembly is null || !Enum.IsDefined(typeof(ModAssemblyHotReloadLevelMirror), m.ModAssembly.HotReloadRawValue)) {
			lv = default;
			return false;
		}
		lv = (ModAssemblyHotReloadLevelMirror)m.ModAssembly.HotReloadRawValue;
		return true;
	}
}
