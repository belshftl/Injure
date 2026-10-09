// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Gpu;

/// <summary>
/// The kind of a <see cref="GpuError"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct GpuErrorKind {
	/// <summary>Raw switch tag for <see cref="GpuErrorKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// A call or the commands it recorded broke WebGPU's validation rules; a bug in the calling
		/// code, or in Injure if Injure's own validation should have caught it.
		/// </summary>
		Validation = 1,

		/// <summary>
		/// An allocation failed.
		/// </summary>
		OutOfMemory,

		/// <summary>
		/// WebGPU failed for a reason that isn't the caller's fault, e.g. a shader that compiled to
		/// something the driver rejected.
		/// </summary>
		Internal,

		/// <summary>
		/// WebGPU reported an error kind Injure doesn't know.
		/// </summary>
		Unknown,
	}
}

/// <summary>
/// An error reported by WebGPU, passed to a <see cref="GpuErrorHandler"/>.
/// </summary>
/// <param name="Kind">The kind of error.</param>
/// <param name="Message">WebGPU's description of the error.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct GpuError(GpuErrorKind Kind, string Message);

/// <summary>
/// Handles an error WebGPU reports on <paramref name="device"/>; see
/// <see cref="GpuDeviceOptions.ErrorHandler"/>.
/// </summary>
/// <param name="device">The device the error happened on.</param>
/// <param name="error">The error.</param>
public delegate void GpuErrorHandler(GpuDevice device, in GpuError error);

/// <summary>
/// Stock <see cref="GpuErrorHandler"/>s.
/// </summary>
public static class GpuErrorHandlers {
	/// <summary>
	/// Terminates the process with <see cref="Environment.FailFast(string)"/>, reporting the error
	/// and the managed stack trace. The default handler.
	/// </summary>
	public static readonly GpuErrorHandler FailFast = static (GpuDevice _, in GpuError error) =>
		Environment.FailFast($"WebGPU {error.Kind} error: {error.Message}");

	/// <summary>
	/// Creates a handler that writes each error to <paramref name="writer"/> and lets the program
	/// continue.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="writer"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// WebGPU keeps working after an error, but the object or commands involved are invalid, so
	/// later operations that use them fail too, typically with more errors.
	/// </remarks>
	public static GpuErrorHandler Log(TextWriter writer) {
		ArgumentNullException.ThrowIfNull(writer);
		return (GpuDevice _, in GpuError error) => {
			lock (writer)
				writer.WriteLine($"WebGPU {error.Kind} error: {error.Message}");
		};
	}
}

/// <summary>
/// Collects reported errors instead of acting on them, for tests and tools that inspect them
/// afterwards. Pass <see cref="Handle"/> as the <see cref="GpuDeviceOptions.ErrorHandler"/>.
/// </summary>
/// <remarks>
/// Thread-safe.
/// </remarks>
public sealed class GpuErrorCollector {
	private readonly Lock errorsLock = new();
	private readonly List<GpuError> errors = new();

	/// <summary>
	/// A <see cref="GpuErrorHandler"/> that records the error.
	/// </summary>
	public void Handle(GpuDevice device, in GpuError error) {
		lock (errorsLock)
			errors.Add(error);
	}

	/// <summary>
	/// Returns the errors recorded so far, in order, and clears them.
	/// </summary>
	public GpuError[] TakeAll() {
		lock (errorsLock) {
			GpuError[] ret = errors.ToArray();
			errors.Clear();
			return ret;
		}
	}
}
