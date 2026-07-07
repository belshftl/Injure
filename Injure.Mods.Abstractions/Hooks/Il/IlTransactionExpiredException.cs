// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Hooks.Il;

public sealed class IlTransactionExpiredException(
	string? ownerId,
	string? localId,
	string? targetMethod,
	string? message
) : InvalidOperationException(message) {
	public string? OwnerId { get; } = ownerId;
	public string? LocalId { get; } = localId;
	public string? TargetMethod { get; } = targetMethod;

	public IlTransactionExpiredException() : this(null, null, null, "the IL manipulation transaction is no longer valid") {
	}

	public IlTransactionExpiredException(string? message) : this(null, null, null, message) {
	}

	public IlTransactionExpiredException(string? ownerId, string? localId, string? targetMethod)
		: this(ownerId, localId, targetMethod, fmt(ownerId, localId, targetMethod)) {
	}

	private static string fmt(string? ownerId, string? localId, string? targetMethod) {
		if (ownerId is null || localId is null)
			return "the IL manipulation transaction is no longer valid";
		if (targetMethod is null)
			return $"IL manipulation transaction '{ownerId}::{localId}' is no longer valid";
		return $"IL manipulation transaction '{ownerId}::{localId}' for '{targetMethod}' is no longer valid";
	}
}
