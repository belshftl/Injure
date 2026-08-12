// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions;

public sealed class ModLoadException : Exception {
	public string? ModOwnerId { get; }

	public ModLoadException(string message) : base(message) {
	}

	public ModLoadException(string modOwnerId, string message) : base($"while loading mod '{modOwnerId}': {message}") {
		ModOwnerId = modOwnerId;
	}
}
