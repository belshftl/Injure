// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure;

/// <summary>
/// Provides the capability to register synchronous resources for ordered disposal
/// when an implementation-defined scope ends.
/// </summary>
/// <remarks>
/// <para>
/// This interface does not define what constitutes the scope, when it ends, which
/// thread ends it, or how an implementation reports or handles disposal failures.
/// Those semantics are defined by the implementing type.
/// </para>
/// <para>
/// Resources are disposed in the reverse order in which they are registered. This
/// is intended to support local ownership relationships such as registering a parent
/// before a child so that the child is disposed first.
/// </para>
/// <para>
/// If registration returns successfully, the implementation must include the resource
/// in disposal when the scope ends. Registration may instead fail with an exception
/// if the scope no longer accepts registrations.
/// </para>
/// <para>
/// This interface does not require implementations to be thread-safe.
/// </para>
/// <para>
/// Implementing this interface does not imply that the object exists solely to act
/// as a disposal scope. It only indicates that the object provides this capability.
/// </para>
/// </remarks>
public interface IDisposalScope {
	/// <summary>
	/// Registers a synchronous resource for ordered disposal in reverse
	/// registration order when the scope ends.
	/// </summary>
	/// <typeparam name="T">
	/// The type of the resource.
	/// </typeparam>
	/// <param name="disposable">
	/// The resource to register.
	/// </param>
	/// <returns>
	/// <paramref name="disposable"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <remarks>
	/// The implementation may throw if the underlying scope has already ended or no
	/// longer accepts registrations.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	T AddOrderedDisposable<T>(T disposable) where T : notnull, IDisposable;
}

/// <summary>
/// Extends <see cref="IDisposalScope"/> with the capability to register asynchronous
/// resources for ordered disposal when an implementation-defined scope ends.
/// </summary>
/// <remarks>
/// <para>
/// This interface inherits the implementation-defined lifetime, invalidation,
/// failure-handling, and thread-safety semantics of <see cref="IDisposalScope"/>.
/// It additionally indicates that the implementing type can perform asynchronous
/// disposal while ending its scope.
/// </para>
/// <para>
/// Synchronous and asynchronous ordered resources participate in one logical
/// registration order.
/// </para>
/// <para>
/// Implementing this interface does not imply that the object exists solely to act
/// as a disposal scope. It only indicates that the object provides these capabilities.
/// </para>
/// </remarks>
public interface IAsyncDisposalScope : IDisposalScope {
	/// <summary>
	/// Registers an asynchronous resource for ordered disposal in reverse
	/// registration order when the scope ends.
	/// </summary>
	/// <typeparam name="T">
	/// The type of the resource.
	/// </typeparam>
	/// <param name="disposable">
	/// The resource to register.
	/// </param>
	/// <returns>
	/// <paramref name="disposable"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Asynchronous disposal is awaited before disposal proceeds to the next resource.
	/// </para>
	/// <para>
	/// The implementation may throw if the underlying scope has already ended or no
	/// longer accepts registrations.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	T AddOrderedAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable;
}

/// <summary>
/// Extends <see cref="IAsyncDisposalScope"/> with the capability to register
/// independent resources for parallel disposal when an implementation-defined
/// scope ends.
/// </summary>
/// <remarks>
/// <para>
/// Resources registered through <see cref="AddDisposable{T}(T)"/> or
/// <see cref="AddAsyncDisposable{T}(T)"/> must not depend on their disposal occurring
/// before or after disposal of another parallel resource. The implementation may
/// dispose such resources concurrently and does not provide a relative ordering
/// guarantee between them.
/// </para>
/// <para>
/// All parallel disposals complete before any ordered disposal begins.
/// </para>
/// <para>
/// The degree of parallelism, scheduling policy, and failure-handling policy are defined
/// by the implementing type. An implementation may use a fixed or configurable
/// parallelism limit.
/// </para>
/// <para>
/// This interface inherits the implementation-defined lifetime, invalidation, and
/// thread-safety semantics of <see cref="IAsyncDisposalScope"/>. It does not itself
/// require implementations to be thread-safe.
/// </para>
/// <para>
/// Implementing this interface does not imply that the object exists solely to act
/// as a disposal scope. It only indicates that the object provides these capabilities.
/// </para>
/// </remarks>
public interface IParallelDisposalScope : IAsyncDisposalScope {
	/// <summary>
	/// Registers a synchronous resource for unordered, potentially parallel disposal
	/// when the scope ends.
	/// </summary>
	/// <typeparam name="T">
	/// The type of the resource.
	/// </typeparam>
	/// <param name="disposable">
	/// The resource to register.
	/// </param>
	/// <returns>
	/// <paramref name="disposable"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <remarks>
	/// <para>
	/// The resource must not rely on a relative disposal order with other resources
	/// registered for parallel disposal. All parallel disposals complete before any
	/// ordered disposal begins.
	/// </para>
	/// <para>
	/// The implementation may throw if the underlying scope has already ended or no
	/// longer accepts registrations.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	T AddDisposable<T>(T disposable) where T : notnull, IDisposable;

	/// <summary>
	/// Registers an asynchronous resource for unordered, potentially parallel disposal
	/// when the scope ends.
	/// </summary>
	/// <typeparam name="T">
	/// The type of the resource.
	/// </typeparam>
	/// <param name="disposable">
	/// The resource to register.
	/// </param>
	/// <returns>
	/// <paramref name="disposable"/>. The passed value is returned back as-is for
	/// construct-and-register ergonomics.
	/// </returns>
	/// <remarks>
	/// <para>
	/// The resource must not rely on a relative disposal order with other resources
	/// registered for parallel disposal. All parallel disposals complete before any
	/// ordered disposal begins.
	/// </para>
	/// <para>
	/// The implementation may throw if the underlying scope has already ended or no
	/// longer accepts registrations.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	T AddAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable;
}
