// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Layers;

namespace TestGame;

public static class LayerTags {
	public static LayerTag Gameplay { get; private set; }

	public static void Init() {
		Gameplay = Game.LayerTagRegistry.GetOrCreate(Game.OwnerID, "gameplay");
	}
}
