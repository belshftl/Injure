// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Injure.Host;

/// <summary>
/// Identifies a window (or other window-like surface) across all host event sources in the process.
/// </summary>
/// <remarks>
/// <para>
/// IDs come from a single process-wide allocator (<see cref="Allocate()"/>), so IDs minted by
/// different event sources do not collide. IDs are never reused within a process.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct HostWindowId : IEquatable<HostWindowId> {
	private static long next = 0;

	private readonly long value;

	private HostWindowId(long value) {
		this.value = value;
	}

	/// <summary>
	/// Whether this is a valid ID, i.e. not <see langword="default"/>.
	/// </summary>
	public bool IsValid => value != 0;

	/// <summary>
	/// Allocates a new, process-wide unique ID.
	/// </summary>
	/// <remarks>
	/// Thread-safe. Meant to be called by whatever creates the window, typically an event source
	/// implementation.
	/// </remarks>
	public static HostWindowId Allocate() => new(Interlocked.Increment(ref next));

	public bool Equals(HostWindowId other) => value == other.value;
	public override bool Equals(object? obj) => obj is HostWindowId other && Equals(other);
	public override int GetHashCode() => value.GetHashCode();
	public static bool operator ==(HostWindowId left, HostWindowId right) => left.value == right.value;
	public static bool operator !=(HostWindowId left, HostWindowId right) => left.value != right.value;

	/// <summary>
	/// Returns a string of the form <c>window#N</c>, or <c>window#invalid</c> for the
	/// <see langword="default"/> value.
	/// </summary>
	public override string ToString() => value != 0 ? "window#" + value.ToString(CultureInfo.InvariantCulture) : "window#invalid";
}
