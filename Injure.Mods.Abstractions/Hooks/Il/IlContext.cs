// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.CodeAnalysis;

namespace Injure.Mods.Abstractions.Hooks.Il;

/// <summary>
/// Transaction-scoped view of the method body presented to an IL manipulator.
/// </summary>
/// <typeparam name="L">
/// Lifetime identity of the owner; see <c>Docs/mods/lifetime-identity.md</c> for more info.
/// </typeparam>
[DontCache]
public sealed class IlContext<L> : IStrongRefDroppable where L : struct, IModLifetimeIdentity {
	private IlTransactionCore? core;
	private readonly string? ownerId;
	private readonly string? localId;
	private readonly string? targetMethod;
	internal IlTransactionCore Core => Volatile.Read(ref core) ?? throw new IlTransactionExpiredException(ownerId, localId, targetMethod);

	internal IlContext(IlTransactionCore core) {
		this.core = core ?? throw new InternalStateException("IlContext constructed with null core");
		ownerId = core.OwnerId;
		localId = core.LocalId;
		targetMethod = core.TargetMethodDisplayName;
	}

	/// <summary>
	/// The amount of instructions in this manipulator's input snapshot.
	/// </summary>
	public int InstructionCount => Core.InstructionCount;

	/// <summary>
	/// Helpers for importing references into the target Cecil module.
	/// </summary>
	public IlReferenceImports Imports => new(Core);

	/// <summary>
	/// Creates an unresolved transaction-local label that can be used by branch emissions before it
	/// is marked.
	/// </summary>
	public IlLabel DefineLabel() => Core.DefineLabel();

	/// <summary>
	/// Emits a fragment before the first instruction in the snapshot.
	/// </summary>
	public void EmitAtStart(IlEmitAction emit) => Core.EmitAtBoundary(0, emit);

	/// <summary>
	/// Emits a fragment after the last instruction in the snapshot.
	/// </summary>
	public void EmitAtEnd(IlEmitAction emit) => Core.EmitAtBoundary(Core.InstructionCount, emit);

	/// <summary>
	/// Finds every occurrence of a non-empty pattern in the snapshot that matches the given
	/// provenance constraint.
	/// </summary>
	/// <remarks>
	/// Matching without a provenance constraint is not allowed; if you explicitly don't care about
	/// provenance, use <see cref="IlPatternProvenanceConstraint.Any"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="pattern"/> is empty, or if <paramref name="provenance"/> is an
	/// invalid/uninitialized value.
	/// </exception>
	public IlMatches MatchAll(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) => Core.MatchAll(pattern, provenance);

	/// <summary>
	/// Finds the first occurrence of a non-empty pattern in the snapshot that matches the given
	/// provenance constraint.
	/// </summary>
	/// <remarks>
	/// Matching without a provenance constraint is not allowed; if you explicitly don't care about
	/// provenance, use <see cref="IlPatternProvenanceConstraint.Any"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="pattern"/> is empty, or if <paramref name="provenance"/> is an
	/// invalid/uninitialized value.
	/// </exception>
	/// <exception cref="IlMatchException">
	/// Thrown if no match is found.
	/// </exception>
	public IlMatch MatchNext(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) => Core.MatchNext(0, pattern, provenance);

	internal void DropStrongReferences() => Volatile.Write(ref core, null);
	void IStrongRefDroppable.DropStrongReferences() => DropStrongReferences();
}
