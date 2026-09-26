// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace Injure.Mods.Runtime;

/// <summary>
/// Records process-lifetime (i.e. non-reloadable) mods that get loaded, and never un-records them.
/// </summary>
internal static class ProcessModRegistry {
	private static readonly ConcurrentDictionary<string, bool> nonReloadableLoaded = new(StringComparer.Ordinal);

	/// <summary>
	/// Records that a non-reloadable mod's code is being loaded into this process.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> if it already was, by this or an earlier runtime; its code can't be
	/// unloaded, so loading it again needs a process restart.
	/// </returns>
	public static bool TryClaimNonReloadable(string ownerId) => nonReloadableLoaded.TryAdd(ownerId, true);
}
