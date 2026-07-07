// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Hooks.Il;

/// <summary>
/// Base exception for invalid IL declarations, failed transformations, and lowering failures.
/// </summary>
public /* open */ class IlPipelineException : Exception {
	internal IlPipelineException(string message) : base(message) {}
	internal IlPipelineException(string message, Exception ex) : base(message, ex) {}
}
