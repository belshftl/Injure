// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

// what borrows and pins refer to: an asset slot or a derived value's slot
internal abstract class VersionSlot {
	public abstract AssetStore Store { get; }
	// pins and returns the current version, loading or computing it first if needed
	public abstract AssetStore.AssetVersion PinCurrentUntyped();
	public abstract bool IsCurrentUntyped(AssetStore.AssetVersion version);
}

internal abstract class VersionSlot<T> : VersionSlot where T : class {
	public abstract bool IsCurrent(AssetStore.AssetVersion<T> version);
	public abstract AssetStore.AssetVersion<T> PinCurrent();
	public override AssetStore.AssetVersion PinCurrentUntyped() => PinCurrent();
	public override bool IsCurrentUntyped(AssetStore.AssetVersion version) => version is AssetStore.AssetVersion<T> v && IsCurrent(v);
}

/// <summary>
/// Non-generic base of <see cref="Versioned{T}"/>, for lists of inputs whose values have different
/// types; see
/// <see cref="DerivedAsset.Create{T}(ReadOnlySpan{Versioned}, DeriveFunc{T}, string?)"/>.
/// </summary>
/// <remarks>
/// Only <see cref="Asset{T}"/> and <see cref="DerivedAsset{T}"/> derive from this.
/// </remarks>
public abstract class Versioned {
	private protected Versioned() {}

	internal abstract VersionSlot UntypedSlot { get; }
}

/// <summary>
/// A value whose versions are managed by an <see cref="AssetStore"/>: an <see cref="Asset{T}"/>, or
/// a <see cref="DerivedAsset{T}"/> computed from others.
/// </summary>
/// <typeparam name="T">Type of the value. Must be a reference type.</typeparam>
/// <remarks>
/// Both kinds hand out their current version the same way: <see cref="Borrow()"/> for use until the
/// borrowing thread's next asset reload boundary, and <see cref="Pin()"/> to keep a version alive
/// until the pin is disposed. APIs that only use the value can take a <see cref="Versioned{T}"/> to
/// accept either. Only <see cref="Asset{T}"/> and <see cref="DerivedAsset{T}"/> derive from this.
/// </remarks>
public abstract class Versioned<T> : Versioned where T : class {
	private protected Versioned() {}

	/// <summary>
	/// Borrows the current version, loading or computing it first if needed. Requires the calling
	/// thread to be attached to the store.
	/// </summary>
	/// <returns>A borrow of the current version.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the calling thread isn't attached to the store; see
	/// <see cref="AssetStore.AttachCurrentThread()"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store, or this value if it's a <see cref="DerivedAsset{T}"/>, has been disposed.
	/// </exception>
	/// <remarks>
	/// Throws whatever loading or computing the value throws, e.g. <see cref="AssetLoadException"/>.
	/// </remarks>
	public abstract AssetBorrow<T> Borrow();

	/// <summary>
	/// Pins the current version, loading or computing it first if needed. Works from any thread,
	/// attached to the store or not.
	/// </summary>
	/// <returns>A pin of the current version, owned by the caller.</returns>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store, or this value if it's a <see cref="DerivedAsset{T}"/>, has been disposed.
	/// </exception>
	/// <remarks>
	/// Throws whatever loading or computing the value throws, e.g. <see cref="AssetLoadException"/>.
	/// </remarks>
	public abstract AssetPin<T> Pin();

	internal abstract VersionSlot<T> Slot { get; }
	internal override VersionSlot UntypedSlot => Slot;
}
