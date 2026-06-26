// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Lifetime;

internal sealed class PendingGoto {
	public required ILabelSymbol Target { get; init; }
	public required FlowState State { get; init; }
	public required Location Location { get; init; }
}
