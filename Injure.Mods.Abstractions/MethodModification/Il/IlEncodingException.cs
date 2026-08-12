// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Exceptiont thrown when a transformed method body cannot be encoded into a CLR method body.
/// </summary>
/// <remarks>
/// This covers token resolution failures, operands that cannot be represented in the target module,
/// and bodies that exceed a metadata encoding limit.
/// </remarks>
public sealed class IlEncodingException : IlPipelineException {
	internal IlEncodingException(string message) : base(message) {
	}

	internal IlEncodingException(string message, Exception ex) : base(message, ex) {
	}
}
