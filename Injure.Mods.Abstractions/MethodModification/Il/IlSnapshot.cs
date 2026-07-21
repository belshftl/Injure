// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal sealed class IlSnapshot(MethodDefinition method, Instruction[] instrs, InternalIlProvenance[] provenance) : IStrongRefDroppable {
	private MethodDefinition? method = method;
	private Instruction[] instrs = instrs;
	private InternalIlProvenance[] provenance = provenance;

	public MethodDefinition Method => method ?? throw new InternalStateException("IlSnapshot used after its strong refs have already been dropped");
	public Instruction[] Instructions => instrs;
	public InternalIlProvenance[] Provenance => provenance;

	public void DropStrongReferences() {
		method = null;
		instrs = Array.Empty<Instruction>();
		provenance = Array.Empty<InternalIlProvenance>();
	}
}
