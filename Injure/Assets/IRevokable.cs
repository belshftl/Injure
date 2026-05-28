// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Allows an asset-backed object to be explicitly invalidated.
/// </summary>
/// <remarks>
/// <para>
/// <b>Revocation is only logical invalidation</b>; the object may still need to be disposed
/// to release held resources.
/// </para>
/// <para>
/// Since use after revocation is a logic bug, implementors are expected to fail fast
/// on any attempt to use the object after <see cref="Revoke()"/>, typically by throwing
/// <see cref="AssetLeaseExpiredException"/>. Implementors that also implement <see cref="System.IDisposable"/>
/// and/or <see cref="System.IAsyncDisposable"/> should <b>not</b> throw on post-revoke disposal.
/// </para>
/// </remarks>
public interface IRevokable {
	/// <summary>
	/// Revokes this object, logically invalidating it and making further use illegal.
	/// </summary>
	/// <remarks>
	/// If this object also implements <see cref="System.IDisposable"/> and/or <see cref="System.IAsyncDisposable"/>,
	/// post-revoke disposal is still safe.
	/// </remarks>
	void Revoke();
}
