// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

/// <summary>
/// Small utility to pair an <see cref="AssetStore"/> with a namespace.
/// </summary>
public readonly struct AssetNamespace {
	private readonly AssetStore store;

	/// <summary>
	/// Asset namespace this object is bound to.
	/// </summary>
	public string Namespace => field ?? throw new InvalidOperationException("invalid AssetNamespace value (did you accidentally use `default`)?");

	internal AssetNamespace(AssetStore store, string ns) {
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(ns);
		AssetID.ValidateNamespaceOrThrow(ns);
		this.store = store;
		Namespace = ns;
	}

	/// <summary>
	/// Gets a stable handle for the specified asset in the namespace.
	/// </summary>
	/// <remarks>
	/// See <see cref="AssetStore.GetAsset{T}(AssetID)"/>; this is a tiny wrapper over it.
	/// </remarks>
	public AssetRef<T> Get<T>(string path) where T : class => store.GetAsset<T>(new AssetID(Namespace, path));

	/// <summary>
	/// Creates an <see cref="AssetID"/> from this <see cref="AssetNamespace"/>'s namespace and the provided path.
	/// </summary>
	public AssetID ID(string path) => new(Namespace, path);
}
