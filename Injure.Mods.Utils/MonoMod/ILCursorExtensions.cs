// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

using Mono.Cecil.Cil;

using MonoMod.Cil;

namespace Injure.Mods.Utils.MonoMod;

/// <summary>
/// Provides utility/convenience extensions for <see cref="ILCursor"/>.
/// </summary>
public static class ILCursorExtensions {
	extension(ILCursor c) {
		/// <summary>
		/// Searches forward and moves the cursor to the next sequence of instructions ahead
		/// of the cursor that are matched by the corresponding predicates, or throws if the
		/// sequence is not found.
		/// </summary>
		/// <param name="expected">
		/// Short, human-readable string describing the instruction(s) being searched for.
		/// </param>
		/// <param name="moveType">
		/// Where the cursor is to be moved relative to the matched instruction(s).
		/// </param>
		/// <param name="predicates">
		/// Predicates used for <see cref="ILCursor"/> instruction matching.
		/// </param>
		/// <exception cref="ILPatternNotFoundException">
		/// Thrown if the expected sequence of instructions was not found.
		/// </exception>
		public void RequireGotoNext(string expected, MoveType moveType = MoveType.Before, params Func<Instruction, bool>[] predicates) {
			int start = c.Index;
			if (c.TryGotoNext(moveType, predicates))
				return;
			string target = c.Context.Method.FullName ?? c.Context.Method.Name ?? "<name unavailable>";
			throw new ILPatternNotFoundException(target, expected, start, ILPatternSearchDirection.Forward);
		}

		/// <summary>
		/// Searches forward and moves the cursor before the next sequence of instructions ahead
		/// of the cursor that are matched by the corresponding predicates, or throws if the
		/// sequence is not found.
		/// </summary>
		/// <inheritdoc cref="RequireGotoNext(ILCursor, string, MoveType, Func{Instruction, bool}[])"/>
		public void RequireGotoNext(string expected, params Func<Instruction, bool>[] predicates) =>
			c.RequireGotoNext(expected, moveType: MoveType.Before, predicates);

		/// <summary>
		/// Searches forward and moves the cursor to the next sequence of instructions behind
		/// the cursor that are matched by the corresponding predicates, or throws if the
		/// sequence is not found.
		/// </summary>
		/// <inheritdoc cref="RequireGotoNext(ILCursor, string, MoveType, Func{Instruction, bool}[])"/>
		public void RequireGotoPrev(string expected, MoveType moveType = MoveType.Before, params Func<Instruction, bool>[] predicates) {
			int start = c.Index;
			if (c.TryGotoPrev(moveType, predicates))
				return;
			string target = c.Context.Method.FullName ?? c.Context.Method.Name ?? "<name unavailable>";
			throw new ILPatternNotFoundException(target, expected, start, ILPatternSearchDirection.Backward);
		}

		/// <summary>
		/// Searches forward and moves the cursor before the next sequence of instructions behind
		/// the cursor that are matched by the corresponding predicates, or throws if the
		/// sequence is not found.
		/// </summary>
		/// <inheritdoc cref="RequireGotoNext(ILCursor, string, MoveType, Func{Instruction, bool}[])"/>
		public void RequireGotoPrev(string expected, params Func<Instruction, bool>[] predicates) =>
			c.RequireGotoPrev(expected, moveType: MoveType.Before, predicates);
	}
}
