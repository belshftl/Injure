// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal sealed class IlMethodBody {
	/// <summary>
	/// Anchor count at or above which lookups build a dictionary rather than scanning linearly.
	/// </summary>
	private const int anchorIndexThreshold = 24;

	private readonly List<IlInstruction> instrs;
	private readonly List<IlAnchorId> anchors;
	private readonly List<IlExceptionRegion> exRegions;
	private Dictionary<IlAnchorId, int>? anchorBoundaries;
	private ulong nextInstrId;
	private ulong nextAnchorId;

	public IlMethodRef Method { get; }
	public bool InitLocals { get; set; }
	public ImmutableArray<IlTypeRef> Locals { get; }

	/// <summary>
	/// Where <see cref="Locals"/> was decoded from, when this body came from metadata. Encoding-only
	/// optimization hint; never part of identity.
	/// </summary>
	public IlLocalSignatureOrigin LocalSignatureOrigin { get; }

	/// <summary>
	/// The validated maximum stack height for the current instruction generation, or
	/// <see langword="null"/> if this generation has not been analyzed.
	/// </summary>
	/// <remarks>
	/// Every operation that changes instructions, locals, exception regions, or the method signature
	/// must reset this to <see langword="null"/>.
	/// </remarks>
	public int? ComputedMaxStack { get; set; }

	public IReadOnlyList<IlInstruction> Instructions => instrs;
	public IReadOnlyList<IlAnchorId> Anchors => anchors;
	public IReadOnlyList<IlExceptionRegion> ExceptionRegions => exRegions;

	public IlMethodBody(
		IlMethodRef method,
		bool initLocals,
		ImmutableArray<IlTypeRef> locals,
		IlLocalSignatureOrigin localSignatureOrigin,
		List<IlInstruction> instrs,
		List<IlAnchorId> anchors,
		List<IlExceptionRegion> exRegions,
		ulong nextInstrId,
		ulong nextAnchorId
	) {
		InternalStateException.ThrowIfNull(method);
		InternalStateException.ThrowIfNull(instrs);
		InternalStateException.ThrowIfNull(anchors);
		InternalStateException.ThrowIfNull(exRegions);
		Method = method;
		InitLocals = initLocals && !locals.IsDefaultOrEmpty;
		Locals = locals.IsDefault ? [] : locals;
		LocalSignatureOrigin = localSignatureOrigin;
		this.instrs = instrs;
		this.anchors = anchors;
		this.exRegions = exRegions;
		this.nextInstrId = nextInstrId;
		this.nextAnchorId = nextAnchorId;
		validateShape();
		validateReferences();
	}

	public static IlMethodBody CreateEmpty(
		IlMethodRef method,
		ImmutableArray<IlTypeRef> locals = default,
		bool initLocals = true
	) {
		InternalStateException.ThrowIfNull(method);
		return new IlMethodBody(method, initLocals, locals, default, [], [new IlAnchorId(1)], [], 0, 1);
	}

	public static IlMethodBody CreateDecoded(
		IlMethodRef method,
		bool initLocals,
		ImmutableArray<IlTypeRef> locals,
		IlLocalSignatureOrigin localSignatureOrigin,
		List<IlInstruction> instrs,
		List<IlAnchorId> anchors,
		List<IlExceptionRegion> exRegions
	) {
		InternalStateException.ThrowIfNull(instrs);
		InternalStateException.ThrowIfNull(anchors);
		ulong nextInstrId = 0;
		foreach (IlInstruction instr in instrs)
			if (instr.Id.Value > nextInstrId)
				nextInstrId = instr.Id.Value;
		ulong nextAnchorId = 0;
		foreach (IlAnchorId anchor in anchors)
			if (anchor.Value > nextAnchorId)
				nextAnchorId = anchor.Value;
		return new IlMethodBody(
			method,
			initLocals,
			locals,
			localSignatureOrigin,
			instrs,
			anchors,
			exRegions,
			nextInstrId,
			nextAnchorId
		);
	}

	public IlAnchorId GetBoundaryAnchor(int boundary) {
		if ((uint)boundary >= (uint)anchors.Count)
			throw new ArgumentOutOfRangeException(nameof(boundary));
		return anchors[boundary];
	}

	public int GetAnchorBoundary(IlAnchorId anchor) {
		if (!TryGetAnchorBoundary(anchor, out int boundary))
			throw new ArgumentException($"anchor {anchor} does not belong to this body", nameof(anchor));
		return boundary;
	}

	public bool TryGetAnchorBoundary(IlAnchorId anchor, out int boundary) {
		if (anchors.Count >= anchorIndexThreshold) {
			anchorBoundaries ??= buildAnchorIndex();
			return anchorBoundaries.TryGetValue(anchor, out boundary);
		}
		for (int i = 0; i < anchors.Count; i++)
			if (anchors[i] == anchor) {
				boundary = i;
				return true;
			}
		boundary = -1;
		return false;
	}

	public IlMethodBody Clone() => new(
		Method,
		InitLocals,
		Locals,
		LocalSignatureOrigin,
		new List<IlInstruction>(instrs),
		new List<IlAnchorId>(anchors),
		new List<IlExceptionRegion>(exRegions),
		nextInstrId,
		nextAnchorId
	) {
		ComputedMaxStack = ComputedMaxStack,
	};

	public IlInstructionId AllocateInstructionId() => new(checked(++nextInstrId));
	public IlAnchorId AllocateAnchorId() => new(checked(++nextAnchorId));

	public void ReplaceInstructions(List<IlInstruction> newInstrs, List<IlAnchorId> newAnchors) {
		InternalStateException.ThrowIfNull(newInstrs);
		InternalStateException.ThrowIfNull(newAnchors);
		if (newAnchors.Count != newInstrs.Count + 1)
			throw new InternalStateException("anchor/instruction shape mismatch");
		instrs.Clear();
		instrs.AddRange(newInstrs);
		anchors.Clear();
		anchors.AddRange(newAnchors);
		anchorBoundaries = null;
		ComputedMaxStack = null;
		validateShape();
		validateReferences();
	}

	private Dictionary<IlAnchorId, int> buildAnchorIndex() {
		Dictionary<IlAnchorId, int> index = new(anchors.Count);
		for (int i = 0; i < anchors.Count; i++)
			if (!index.TryAdd(anchors[i], i))
				throw new InternalStateException($"method body contains duplicate anchor {anchors[i]}");
		return index;
	}

	private void validateShape() {
		if (anchors.Count != instrs.Count + 1)
			throw new InternalStateException("a method body requires exactly one more anchor than instruction");

		HashSet<IlAnchorId> seenAnchors = new(anchors.Count);
		foreach (IlAnchorId anchor in anchors) {
			if (!anchor.IsValid)
				throw new InternalStateException("method body anchors must be valid");
			if (!seenAnchors.Add(anchor))
				throw new InternalStateException($"method body contains duplicate anchor {anchor}");
		}

		HashSet<IlInstructionId> seenIds = new(instrs.Count);
		foreach (IlInstruction instr in instrs) {
			if (!instr.Id.IsValid)
				throw new InternalStateException("method body instruction IDs must be valid");
			if (!seenIds.Add(instr.Id))
				throw new InternalStateException($"method body contains duplicate instruction ID {instr.Id}");
			if (instr.Operand is null)
				throw new InternalStateException($"instruction {instr.Id} has a null operand");
		}
	}

	private void validateReferences() {
		foreach (IlInstruction instr in instrs)
			switch (instr.Operand) {
			case IlBranchOperand branch when !TryGetAnchorBoundary(branch.Target, out _):
				throw new InternalStateException($"instruction {instr.Id} targets unknown anchor {branch.Target}");
			case IlSwitchOperand @switch:
				foreach (IlAnchorId target in @switch.Targets)
					if (!TryGetAnchorBoundary(target, out _))
						throw new InternalStateException($"instruction {instr.Id} targets unknown switch anchor {target}");
				break;
			}

		foreach (IlExceptionRegion region in exRegions) {
			if (
				!TryGetAnchorBoundary(region.TryStart, out int tryStart) ||
				!TryGetAnchorBoundary(region.TryEnd, out int tryEnd) ||
				!TryGetAnchorBoundary(region.HandlerStart, out int handlerStart) ||
				!TryGetAnchorBoundary(region.HandlerEnd, out int handlerEnd)
			)
				throw new InternalStateException("exception region references an anchor outside the method body");
			if (region.FilterStart is IlAnchorId filter && !TryGetAnchorBoundary(filter, out _))
				throw new InternalStateException("exception region references a filter anchor outside the method body");
			if (tryStart >= tryEnd)
				throw new InternalStateException("exception region try range is empty or reversed");
			if (handlerStart >= handlerEnd)
				throw new InternalStateException("exception region handler range is empty or reversed");
			if (region.Kind == IlExceptionRegionKind.Catch && region.CatchType is null)
				throw new InternalStateException("catch region has no catch type");
			if (region.Kind == IlExceptionRegionKind.Filter && region.FilterStart is null)
				throw new InternalStateException("filter region has no filter start");
		}
	}
}
