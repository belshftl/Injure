// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Marks an asset value as held by an <see cref="AssetStore"/>, from when the store publishes it
/// until the store reclaims it.
/// </summary>
/// <remarks>
/// <para>
/// Only the store creates and releases custodies, so a value can rely on <see cref="IsReleased"/>:
/// while it's <see langword="false"/>, the store holds the value and nothing else may dispose it.
/// See <see cref="IAssetStoreValue"/> and <see cref="IAssetStoreDisposable"/>.
/// </para>
/// <para>
/// The <see cref="ToString()"/> of this type describes the version, which can be used for e.g.
/// exception messages.
/// </para>
/// </remarks>
public sealed class AssetStoreCustody {
	private readonly string description;
	private int released = 0;

	internal AssetStoreCustody(AssetId? assetId, string description, ulong version) {
		AssetId = assetId;
		this.description = description;
		Version = version;
	}

	/// <summary>
	/// ID of the asset the value is a version of, or <see langword="null"/> if it's a version of a
	/// <see cref="DerivedAsset{T}"/>.
	/// </summary>
	public AssetId? AssetId { get; }

	/// <summary>
	/// The version number of the value, as in <see cref="AssetBorrow{T}.Version"/>.
	/// </summary>
	public ulong Version { get; }

	/// <summary>
	/// Whether the store has released the value, i.e. reclaimed it or is about to.
	/// </summary>
	public bool IsReleased => Volatile.Read(ref released) != 0;

	internal void Release() => Volatile.Write(ref released, 1);

	/// <summary>
	/// Throws if this custody isn't released yet; meant for the start of
	/// <see cref="IDisposable.Dispose()"/> implementations of <see cref="IAssetStoreDisposable"/>,
	/// called as <c>custody?.ThrowIfHeld(this)</c> so that values never adopted by a store pass.
	/// </summary>
	/// <param name="value">The value being disposed, for the exception message.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this custody isn't released.
	/// </exception>
	public void ThrowIfHeld(object value) {
		if (!IsReleased)
			throw new InvalidOperationException($"can't dispose this {value.GetType().Name}, since it's {this} and the asset store owns it");
	}

	/// <summary>
	/// Describes the version, e.g. for exception messages.
	/// </summary>
	public override string ToString() => description;
}

/// <summary>
/// An asset value that wants to know when an <see cref="AssetStore"/> holds it.
/// </summary>
/// <remarks>
/// <para>
/// Asset types without resources to release don't need this; a type implements it to check
/// <see cref="AssetStoreCustody.IsReleased"/> itself, e.g. to fail on use after reclamation. Types
/// with resources to release implement <see cref="IAssetStoreDisposable"/> instead.
/// </para>
/// </remarks>
public interface IAssetStoreValue {
	/// <summary>
	/// Called once by the asset store when it publishes the value; the value keeps
	/// <paramref name="custody"/> to check later.
	/// </summary>
	/// <param name="custody">The value's custody.</param>
	/// <remarks>
	/// Implement this through an explicit interface implementation, since it's only meant to be called
	/// by the store. It's called with the store's slot lock held, so it must only store the custody.
	/// </remarks>
	void AttachCustody(AssetStoreCustody custody);
}

/// <summary>
/// An asset value with resources to release, which the asset store disposes when reclaiming it.
/// </summary>
/// <remarks>
/// <para>
/// Every asset type implementing <see cref="IDisposable"/> must implement this instead; the store
/// rejects asset types and values that implement <see cref="IDisposable"/> alone, since nothing
/// would stop game code from disposing them while the store holds them.
/// </para>
/// <para>
/// <see cref="IDisposable.Dispose()"/> throws <see cref="InvalidOperationException"/> while
/// <see cref="IsAssetStoreOwned"/> is <see langword="true"/>;
/// <see cref="AssetStoreCustody.ThrowIfHeld(object)"/> implements that check. The store releases
/// the custody before disposing the value, so its own disposal goes through.
/// </para>
/// </remarks>
public interface IAssetStoreDisposable : IAssetStoreValue, IDisposable {
	/// <summary>
	/// Whether an asset store holds this value, i.e. a custody is attached and not released.
	/// </summary>
	bool IsAssetStoreOwned { get; }
}
