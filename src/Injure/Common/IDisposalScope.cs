// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Common;

/// <summary>
/// Allows registering <see cref="IDisposable"/>s to be disposed in reverse registration order when
/// some scope ends.
/// </summary>
/// <remarks>
/// <para>
/// The implementing type defines what the scope is, when and on which thread it ends, and how
/// failures are reported. Implementing this interface doesn't necessarily mean that the object
/// exists only to act as a disposal scope.
/// </para>
/// <para>
/// New <see cref="IDisposable"/>s are registered via <see cref="AddDisp{T}(T)"/>. When the scope
/// ends, every registered object is disposed in reverse registration order, so e.g. a child
/// registered after its parent is disposed before the parent. Every registered object is disposed
/// even if an earlier one's <see cref="IDisposable.Dispose()"/> call throws; whether/how failures
/// are aggregated and reported is up to the implementing type.
/// </para>
/// <para>
/// Registering after the scope has ended always throws, but the exception type is up to the
/// implementing type.
/// </para>
/// <para>
/// Implementations need not be thread safe.
/// </para>
/// </remarks>
public interface IDisposalScope {
	/// <summary>
	/// Registers an <see cref="IDisposable"/> object to be disposed when the scope ends.
	/// </summary>
	/// <typeparam name="T">
	/// Type of the object. This method is generic to allow returning back the input without erasing
	/// the type, not as an optimization; an implementation will usually end up boxing structs to the
	/// interface type either way, in order to store a collection of <see cref="IDisposable"/>s.
	/// </typeparam>
	/// <param name="disposable">The object to register.</param>
	/// <returns>
	/// <paramref name="disposable"/>, returned as-is for construct-and-register ergonomics.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Throws if the scope has already ended; the type of the thrown exception is up to the
	/// implementing type. See type-level docs.
	/// </remarks>
	T AddDisp<T>(T disposable) where T : notnull, IDisposable;
}
