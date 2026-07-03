// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods;

public sealed class ModLoadException : Exception {
	public string? ModOwnerID { get; }

	public ModLoadException(string message) : base(message) {
	}

	public ModLoadException(string modOwnerID, string message) : base($"while loading mod '{modOwnerID}': {message}") {
		ModOwnerID = modOwnerID;
	}
}
