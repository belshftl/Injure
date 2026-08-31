// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// The kind of a protected region, which determines how its handler is entered and which of an
/// <see cref="IlExceptionRegion"/>'s optional members are meaningful.
/// </summary>
internal enum IlExceptionRegionKind : byte {
	/// <summary>
	/// A typed catch handler. Entered with the exception object on the stack.
	/// </summary>
	Catch,

	/// <summary>
	/// A filtered catch handler. Both the filter expression and the handler are entered with the
	/// exception object on the stack.
	/// </summary>
	Filter,

	/// <summary>
	/// A handler run on both normal and exceptional exit from the protected region. Entered with an
	/// empty stack.
	/// </summary>
	Finally,

	/// <summary>
	/// A handler run only on exceptional exit from the protected region. Entered with an empty stack.
	/// </summary>
	Fault,
}

/// <summary>
/// One protected region and its handler, expressed in anchors rather than IL offsets.
/// </summary>
/// <remarks>
/// <para>
/// Every range is half-open. A filter expression occupies the range from <paramref name="FilterStart"/>
/// up to <paramref name="HandlerStart"/>; it has no separate end.
/// </para>
/// <para>
/// Storing anchors rather than offsets is what lets a region survive arbitrary edits: anchors are
/// preserved across commits, so instructions inserted inside a region stay inside it and the region
/// needs no adjustment. It also fixes where an insertion at a region edge lands. Emitting at the
/// boundary an anchor names places the new instructions before that anchor, so emitting at
/// <paramref name="TryStart"/> lands outside the protected region, while emitting at
/// <paramref name="TryEnd"/>, <paramref name="HandlerStart"/>, or <paramref name="HandlerEnd"/>
/// lands inside the range that ends there.
/// </para>
/// <para>
/// Regions are not yet mutable through the authoring API. A manipulator can currently add instructions inside an
/// existing region but cannot create, remove, or retarget one. An exception region authoring API is planned.
/// </para>
/// </remarks>
/// <param name="Kind">Which handler kind this region declares.</param>
/// <param name="TryStart">First boundary of the protected region.</param>
/// <param name="TryEnd">Boundary one past the end of the protected region.</param>
/// <param name="HandlerStart">First boundary of the handler.</param>
/// <param name="HandlerEnd">Boundary one past the end of the handler.</param>
/// <param name="FilterStart">
/// First boundary of the filter expression for <see cref="IlExceptionRegionKind.Filter"/>, or
/// <see langword="null"/> for every other kind.
/// </param>
/// <param name="CatchType">
/// The caught type for <see cref="IlExceptionRegionKind.Catch"/>, or <see langword="null"/> for
/// every other kind.
/// </param>
internal readonly record struct IlExceptionRegion(
	IlExceptionRegionKind Kind,
	IlAnchorId TryStart,
	IlAnchorId TryEnd,
	IlAnchorId HandlerStart,
	IlAnchorId HandlerEnd,
	IlAnchorId? FilterStart,
	IlTypeRef? CatchType
);
