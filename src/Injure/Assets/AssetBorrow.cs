// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// A borrowed asset version, valid until the borrowing thread's next asset reload boundary.
/// </summary>
/// <typeparam name="T">Asset type. Must be a reference type.</typeparam>
/// <remarks>
/// <para>
/// Copying <see cref="Value"/> somewhere that outlives the borrow is a bug; if you need to do that,
/// what you're most likely looking for is a pin (see <see cref="AssetPin{T}"/>). A borrow can be
/// upgraded to a pin via <see cref="Pin()"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly ref struct AssetBorrow<T> : IHasInvalidDefault where T : class {
	private readonly VersionSlot<T>? slot;
	private readonly AssetStore.AssetVersion<T>? version;
	private readonly AssetThreadAttachment? attachment;
	private readonly ulong boundary;

	internal AssetBorrow(VersionSlot<T> slot, AssetStore.AssetVersion<T> version, AssetThreadAttachment attachment) {
		this.slot = slot;
		this.version = version;
		this.attachment = attachment;
		boundary = attachment.BoundaryCount;
	}

	/// <summary>
	/// The borrowed value.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the borrowing thread has passed a reload boundary since the borrow.
	/// </exception>
	public T Value => chk().Value;

	/// <summary>
	/// The borrowed version's version number. Version numbers start at 1 and monotonically increment
	/// with each new version. Never 0.
	/// </summary>
	/// <inheritdoc cref="Value" path="/exception"/>
	public ulong Version => chk().Version;

	/// <summary>
	/// The dependencies collected while creating the borrowed version.
	/// </summary>
	/// <inheritdoc cref="Value" path="/exception"/>
	public ReadOnlySpan<IAssetDep> Deps => chk().Deps.AsSpan();

	/// <summary>
	/// Pins the borrowed version, keeping it alive past the borrow.
	/// </summary>
	/// <returns>A pin of the borrowed version, owned by the caller.</returns>
	/// <inheritdoc cref="Value" path="/exception"/>
	public AssetPin<T> Pin() {
		AssetStore.AssetVersion<T> v = chk();
		// can't race with reclamation: the version stays alive at least until this thread's next
		// boundary, and this thread is still before it
		v.AddPin();
		return new AssetPin<T>(slot!, v);
	}

	private AssetStore.AssetVersion<T> chk() {
		if (version is null)
			throw InvalidDefaultException.For<AssetBorrow<T>>();
		if (attachment!.BoundaryCount != boundary)
			throw new InvalidOperationException($"{version.Describe()} was borrowed before an asset reload boundary of the borrowing thread, and used after it");
		return version;
	}

	bool IHasInvalidDefault.IsDefault => version is null;
}
