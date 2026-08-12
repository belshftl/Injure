// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// The method body as it stood when one manipulator's transaction opened.
/// </summary>
/// <remarks>
/// <para>
/// Everything a manipulator observes comes from here, not from the working body: match boundaries,
/// instruction contents, and provenance are all read from the snapshot. Edits declared during the
/// transaction are buffered and applied only at commit, so a manipulator can emit and keep matching
/// without invalidating boundaries it has already obtained, and an <see cref="IlMatch"/> stays
/// meaningful for the whole transaction.
/// </para>
/// <para>
/// The instruction and anchor arrays are copied, so later commits do not disturb them. Their
/// contents are not deep-copied since it's unnecessary (all of the contained contents are either
/// copied by value or immutable).
/// </para>
/// <para>
/// The commit step relies on this being the pre-transaction state, since it reattaches each of these
/// anchors to the boundary it named when the transaction opened.
/// </para>
/// </remarks>
internal sealed class IlSnapshot {
	/// <summary>
	/// The method the snapshot was taken from.
	/// </summary>
	public IlMethodRef Method { get; }

	/// <summary>
	/// The instructions as they stood when the transaction opened.
	/// </summary>
	public IlInstruction[] Instructions { get; }

	/// <summary>
	/// The boundary anchors as they stood when the transaction opened; one longer than
	/// <see cref="Instructions"/>.
	/// </summary>
	public IlAnchorId[] Anchors { get; }

	internal IlSnapshot(IlMethodBody body) {
		InternalStateException.ThrowIfNull(body);
		Method = body.Method;
		Instructions = body.Instructions.ToArray();
		Anchors = body.Anchors.ToArray();
	}
}
