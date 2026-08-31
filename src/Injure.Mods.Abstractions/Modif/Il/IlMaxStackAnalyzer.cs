// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Height-only stack analyzer.
/// </summary>
/// <remarks>
/// <para>
/// The analysis runs in two passes. The first strict pass walks everything reachable from the method entry,
/// the exception handlers, and the filter expressions. The second, lenient pass re-seeds every boundary that
/// is still unvisited at height 0 and walks it. The lenient pass contributes to the reported maximum but
/// never rejects. The result is therefore never lower than the true maximum from reachable code, and no
/// valid program is rejected.
/// </para>
/// <para>
/// This analyzer tracks heights only. It does not track types and does not produce diagnostics;
/// that is the job of a future diagnostic analyzer.
/// </para>
/// </remarks>
internal static class IlMaxStackAnalyzer {
	public static int Analyze(IlMethodBody body) {
		InternalStateException.ThrowIfNull(body);
		if (body.ComputedMaxStack is int cached)
			return cached;
		int computed = new Analysis(body).Run();
		body.ComputedMaxStack = computed;
		return computed;
	}

	private sealed class Analysis(IlMethodBody body) {
		private const int unknownHeight = int.MinValue;

		private readonly IlMethodBody body = body;
		private readonly IReadOnlyList<IlInstruction> instrs = body.Instructions;
		private readonly int[] heights = createHeights(body.Instructions.Count + 1);
		private readonly Stack<int> work = new();
		private int max;
		private bool strict = true;

		public int Run() {
			seed(0, 0);
			foreach (IlExceptionRegion region in body.ExceptionRegions)
				switch (region.Kind) {
				case IlExceptionRegionKind.Catch:
					seed(boundaryOf(region.HandlerStart), 1);
					break;
				case IlExceptionRegionKind.Filter:
					seed(boundaryOf(region.FilterStart ??
						throw new IlInvalidMethodException("filter region has no filter start")), 1);
					seed(boundaryOf(region.HandlerStart), 1);
					break;
				case IlExceptionRegionKind.Finally:
				case IlExceptionRegionKind.Fault:
					seed(boundaryOf(region.HandlerStart), 0);
					break;
				default:
					throw new InternalStateException($"unknown exception region kind '{region.Kind}'");
				}
			drain();
			validateExRegions();

			strict = false;
			for (int boundary = 0; boundary < instrs.Count; boundary++)
				if (heights[boundary] == unknownHeight) {
					seed(boundary, 0);
					drain();
				}

			return max;
		}

		private static int[] createHeights(int length) {
			int[] heights = new int[length];
			Array.Fill(heights, unknownHeight);
			return heights;
		}

		private void seed(int boundary, int height) {
			if (heights[boundary] == unknownHeight) {
				heights[boundary] = height;
				work.Push(boundary);
				if (height > max)
					max = height;
				return;
			}
			if (strict && heights[boundary] != height)
				throw new IlInvalidMethodException(
					$"stack height {height} conflicts with previously computed height {heights[boundary]}",
					boundary
				);
		}

		private void drain() {
			while (work.Count > 0) {
				int boundary = work.Pop();
				step(boundary, heights[boundary]);
			}
		}

		private void step(int boundary, int height) {
			if (boundary == instrs.Count) {
				if (strict)
					throw new IlInvalidMethodException(
						"control flow reaches the end of the method body without a terminal instruction",
						boundary
					);
				return;
			}

			IlInstruction instr = instrs[boundary];
			IlOpCodeDescriptor desc = IlOpCodeInfo.GetDescriptor(instr.OpCode);
			if (!desc.IsDefined)
				throw new InternalStateException($"instruction {instr.Id} has undefined opcode 0x{(int)instr.OpCode:x4}");
			if (desc.Flow == IlFlowKind.Prefix)
				throw new InternalStateException($"instruction {instr.Id} is a standalone prefix; prefixes must be bundled onto their instruction");

			(int pop, int push) = stackEffect(instr, desc, boundary);
			int after = height - pop;
			if (after < 0) {
				if (strict)
					throw new IlInvalidMethodException(
						$"{displayOpCode(instr.OpCode)} pops {pop} value(s) from an stack of height {height}",
						boundary,
						instr.Id
					);
				after = 0;
			}
			after += push;
			if (after > max)
				max = after;

			if (strict && instr.Prefixes is { } prefixes && prefixes.Has(IlPrefixFlags.Tail) &&
				(boundary + 1 >= instrs.Count || instrs[boundary + 1].OpCode != ILOpCode.Ret))
				throw new IlInvalidMethodException("a tail. prefixed call must be immediately followed by ret", boundary, instr.Id);

			switch (desc.Flow) {
			case IlFlowKind.Next:
				seed(boundary + 1, after);
				break;
			case IlFlowKind.Branch:
				seed(branchTarget(instr, boundary), after);
				break;
			case IlFlowKind.ConditionalBranch:
				seed(branchTarget(instr, boundary), after);
				seed(boundary + 1, after);
				break;
			case IlFlowKind.Switch:
				foreach (int target in switchTargets(instr, boundary))
					seed(target, after);
				seed(boundary + 1, after);
				break;
			case IlFlowKind.Leave:
				// leave empties the stack before transferring control
				seed(branchTarget(instr, boundary), 0);
				break;
			case IlFlowKind.Return:
				requireTerminalHeight(after, 0, "ret", boundary, instr.Id);
				break;
			case IlFlowKind.EndFilter:
				requireTerminalHeight(after, 0, "endfilter", boundary, instr.Id);
				break;
			case IlFlowKind.EndFinally:
				requireTerminalHeight(height, 0, "endfinally", boundary, instr.Id);
				break;
			case IlFlowKind.Jmp:
				requireTerminalHeight(height, 0, "jmp", boundary, instr.Id);
				break;
			case IlFlowKind.Throw:
				break;
			default:
				throw new InternalStateException($"unknown IL flow kind '{desc.Flow}'");
			}
		}

		private void requireTerminalHeight(int height, int expected, string display, int boundary, IlInstructionId id) {
			if (strict && height != expected)
				throw new IlInvalidMethodException(
					$"{display} requires an stack height of {expected}, but the height is {height}",
					boundary,
					id
				);
		}

		private (int Pop, int Push) stackEffect(IlInstruction instr, in IlOpCodeDescriptor descriptor, int boundary) {
			if (descriptor.Pop != IlOpCodeInfo.VariableStackEffect && descriptor.Push != IlOpCodeInfo.VariableStackEffect)
				return (descriptor.Pop, descriptor.Push);

			switch (instr.OpCode) {
			case ILOpCode.Call:
			case ILOpCode.Callvirt: {
				IlMethodSignature signature = methodSignature(instr, boundary);
				return (argSlots(signature, includeThis: true), signature.ReturnType.IsVoid ? 0 : 1);
			}
			case ILOpCode.Newobj: {
				IlMethodSignature signature = methodSignature(instr, boundary);
				return (argSlots(signature, includeThis: false), 1);
			}
			case ILOpCode.Calli: {
				if (instr.Operand is not IlCallSiteOperand callSite)
					throw new IlInvalidMethodException("calli does not have a call-site signature operand", boundary, instr.Id);
				return (argSlots(callSite.Signature, includeThis: true) + 1, callSite.Signature.ReturnType.IsVoid ? 0 : 1);
			}
			case ILOpCode.Ret:
				return (body.Method.Signature.ReturnType.IsVoid ? 0 : 1, 0);
			default:
				throw new InternalStateException($"opcode {instr.OpCode} has a variable stack effect but no rule");
			}
		}

		private static IlMethodSignature methodSignature(IlInstruction instr, int boundary) =>
			instr.Operand is IlMethodOperand method
				? method.Method.Signature
				: throw new IlInvalidMethodException($"{displayOpCode(instr.OpCode)} does not have a method operand", boundary, instr.Id);

		private static int argSlots(IlMethodSignature signature, bool includeThis) =>
			signature.ParameterTypes.Length + (includeThis && signature.HasThis && !signature.ExplicitThis ? 1 : 0);

		private int branchTarget(IlInstruction instr, int boundary) {
			if (instr.Operand is not IlBranchOperand branch)
				throw new IlInvalidMethodException(
					$"{displayOpCode(instr.OpCode)} does not have a branch operand", boundary, instr.Id);
			if (!body.TryGetAnchorBoundary(branch.Target, out int target))
				throw new IlInvalidMethodException(
					$"{displayOpCode(instr.OpCode)} targets anchor {branch.Target}, which does not belong to this body",
					boundary,
					instr.Id
				);
			return target;
		}

		private int[] switchTargets(IlInstruction instr, int boundary) {
			if (instr.Operand is not IlSwitchOperand @switch)
				throw new IlInvalidMethodException("switch does not have a switch operand", boundary, instr.Id);
			int[] targets = new int[@switch.Targets.Length];
			for (int i = 0; i < targets.Length; i++)
				if (!body.TryGetAnchorBoundary(@switch.Targets[i], out targets[i]))
					throw new IlInvalidMethodException(
						$"switch targets anchor {@switch.Targets[i]}, which does not belong to this body",
						boundary,
						instr.Id
					);
			return targets;
		}

		private int boundaryOf(IlAnchorId anchor) =>
			body.TryGetAnchorBoundary(anchor, out int boundary)
				? boundary
				: throw new IlInvalidMethodException($"anchor {anchor} does not belong to this body");

		private void validateExRegions() {
			// pretty barebones, maybe make this fancier but most of it should be the job of the
			// diagnostic analyzer
			foreach (IlExceptionRegion region in body.ExceptionRegions) {
				int tryStart = boundaryOf(region.TryStart);
				if (heights[tryStart] != unknownHeight && heights[tryStart] != 0)
					throw new IlInvalidMethodException(
						$"a protected region is entered with an stack height of {heights[tryStart]}",
						tryStart
					);
			}

			for (int boundary = 0; boundary < instrs.Count; boundary++) {
				if (heights[boundary] == unknownHeight)
					continue;
				switch (IlOpCodeInfo.GetDescriptor(instrs[boundary].OpCode).Flow) {
				case IlFlowKind.EndFinally when !isInHandlerOfKind(boundary, IlExceptionRegionKind.Finally, IlExceptionRegionKind.Fault):
					throw new IlInvalidMethodException("endfinally is not inside a finally or fault handler", boundary, instrs[boundary].Id);
				case IlFlowKind.EndFilter when !isInFilter(boundary):
					throw new IlInvalidMethodException("endfilter is not inside a filter expression", boundary, instrs[boundary].Id);
				}
			}
		}

		private bool isInHandlerOfKind(int boundary, IlExceptionRegionKind first, IlExceptionRegionKind second) {
			foreach (IlExceptionRegion region in body.ExceptionRegions) {
				if (region.Kind != first && region.Kind != second)
					continue;
				if (boundary >= boundaryOf(region.HandlerStart) && boundary < boundaryOf(region.HandlerEnd))
					return true;
			}
			return false;
		}

		private bool isInFilter(int boundary) {
			foreach (IlExceptionRegion region in body.ExceptionRegions) {
				if (region.Kind != IlExceptionRegionKind.Filter || region.FilterStart is not IlAnchorId filterStart)
					continue;
				if (boundary >= boundaryOf(filterStart) && boundary < boundaryOf(region.HandlerStart))
					return true;
			}
			return false;
		}

		private static string displayOpCode(ILOpCode opCode) => opCode.ToString().Replace('_', '.').ToLowerInvariant();
	}
}
