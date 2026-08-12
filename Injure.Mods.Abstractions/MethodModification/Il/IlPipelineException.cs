// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Base exception for invalid IL declarations, failed transformations, and lowering failures.
/// </summary>
/// <remarks>
/// This is the common base for every way a patch can fail to apply: a pattern that did not match,
/// a transformed body that did not validate, and a body that could not be encoded. Catching it
/// catches all of them.
/// </remarks>
public /* open */ class IlPipelineException : Exception {
	internal IlPipelineException(string message) : base(message) {}
	internal IlPipelineException(string message, Exception ex) : base(message, ex) {}
}
