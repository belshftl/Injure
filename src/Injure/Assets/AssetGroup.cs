// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

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
/// Residencies take a snapshot of <see cref="Members"/> when they're created, so a group whose
/// members change afterwards doesn't affect existing residencies.
/// </para>
/// </remarks>
public abstract class AssetGroup {
	/// <summary>
	/// The assets in this group. Implementations return the same assets every time, unless the group
	/// is meant to change.
	/// </summary>
	public abstract IReadOnlyList<IUntypedAsset> Members { get; }
}

/// <summary>
/// An <see cref="AssetGroup"/> with members given at runtime, e.g. assets listed by a level file,
/// or several groups combined.
/// </summary>
public sealed class AssetSet : AssetGroup {
	private readonly IUntypedAsset[] members;

	/// <summary>
	/// Creates a group of <paramref name="members"/>, deduplicated.
	/// </summary>
	/// <param name="members">The assets.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="members"/> or one of its elements is <see langword="null"/>.
	/// </exception>
	public AssetSet(IEnumerable<IUntypedAsset> members) {
		ArgumentNullException.ThrowIfNull(members);
		// handles are canonical per asset, so reference equality finds duplicates
		this.members = members.Select(static m => m ?? throw new ArgumentNullException(nameof(members), "asset set members must not be null"))
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
			all.AddRange(g.Members);
		}
		return new AssetSet(all);
	}

	/// <inheritdoc/>
	public override IReadOnlyList<IUntypedAsset> Members => members;
}
