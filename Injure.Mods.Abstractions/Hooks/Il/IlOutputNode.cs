// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

internal abstract record IlOutputNode;

internal sealed record IlOutputInstructionNode(Instruction Instruction, InternalIlProvenance Provenance, IlInstructionSpec? PendingSpec) : IlOutputNode;

internal sealed record IlOutputLabelNode(int LabelId) : IlOutputNode;
