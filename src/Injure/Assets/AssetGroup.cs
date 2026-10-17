// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections;

namespace Injure.Assets;

/// <summary>
/// A set of assets that are loaded/unloaded together, for example "everything a layer uses"; see
/// <see cref="AssetGroupExtensions.MakeResidentAsync{TGroup}(TGroup, IProgress{float}?, int, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// The asset catalog generator emits one sealed subclass per subdirectory of an asset directory,
/// whose members are the subdirectory's assets, including those of its nested subdirectories. A
/// hand-written derived class defines a group in code, and <see cref="AssetSet"/> is a group whose
/// members are decided at runtime.
/// </para>
/// <para>
/// The members are only reachable through <see cref="IReadOnlyList{T}"/>, which this class
/// implements explicitly, e.g. by <see langword="foreach"/> or LINQ. That's so that the group adds
/// no public member names of its own, which would collide with the asset properties of generated
/// derived classes.
/// </para>
/// <para>
/// Residencies take a snapshot of the members when they're created, so a group whose members change
/// afterwards doesn't affect existing residencies.
/// </para>
/// </remarks>
public abstract class AssetGroup : IReadOnlyList<IUntypedAsset> {
	private readonly IReadOnlyList<IUntypedAsset> members;

	/// <summary>
	/// Creates a group of <paramref name="members"/>.
	/// </summary>
	/// <param name="members">
	/// The assets in this group. The group keeps the list rather than copying it, so a derived class
	/// can fill it after this constructor runs, or change it later if the group is meant to change.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="members"/> is <see langword="null"/>.
	/// </exception>
	protected AssetGroup(IReadOnlyList<IUntypedAsset> members) {
		ArgumentNullException.ThrowIfNull(members);
		this.members = members;
	}

	int IReadOnlyCollection<IUntypedAsset>.Count => members.Count;
	IUntypedAsset IReadOnlyList<IUntypedAsset>.this[int index] => members[index];
	IEnumerator<IUntypedAsset> IEnumerable<IUntypedAsset>.GetEnumerator() => members.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => members.GetEnumerator();
}

/// <summary>
/// An <see cref="AssetGroup"/> with members given at runtime, e.g. assets listed by a level file,
/// or several groups combined.
/// </summary>
/// <remarks>
/// Creates a group of <paramref name="members"/>, deduplicated.
/// </remarks>
/// <param name="members">The assets.</param>
/// <exception cref="ArgumentNullException">
/// Thrown if <paramref name="members"/> or one of its elements is <see langword="null"/>.
/// </exception>
public sealed class AssetSet(IEnumerable<IUntypedAsset> members) : AssetGroup(dedup(members)) {
	private static IUntypedAsset[] dedup(IEnumerable<IUntypedAsset> members) {
		ArgumentNullException.ThrowIfNull(members);
		// handles are canonical per asset, so reference equality finds duplicates
		return members.Select(static m => m ?? throw new ArgumentNullException(nameof(members), "asset set members must not be null"))
			.Distinct(ReferenceEqualityComparer.Instance)
			.Cast<IUntypedAsset>()
			.ToArray();
	}

	/// <summary>
	/// Creates a group of every member of <paramref name="groups"/>, deduplicated.
	/// </summary>
	/// <param name="groups">The groups to combine.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if one of <paramref name="groups"/> is <see langword="null"/>.
	/// </exception>
	public static AssetSet Of(params ReadOnlySpan<AssetGroup> groups) {
		List<IUntypedAsset> all = new();
		foreach (AssetGroup g in groups) {
			ArgumentNullException.ThrowIfNull(g, nameof(groups));
			all.AddRange(g);
		}
		return new AssetSet(all);
	}
}
