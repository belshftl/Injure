// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.ComponentModel;

using static Injure.Native.PreciseWait;

namespace Injure.Timing;

/// <summary>
/// Simple utility for high-precision sleep.
/// </summary>
/// <remarks>
/// The implementation is native and platform-dependent:
/// <list type="bullet">
/// <item><description>On Windows, this is implemented using <c>WaitableTimer</c> objects.</description></item>
/// <item><description>On MacOS, this is implemented using <c>mach_wait_until()</c>.</description></item>
/// <item><description>On Linux and FreeBSD, this is implemented using <c>clock_nanosleep(2)</c>.</description></item>
/// </list>
/// </remarks>
public static class PreciseWait {
	private static void wrap(string fn, int rv) {
		if (rv != 0) {
			if (OperatingSystem.IsWindows())
				throw new Win32Exception(rv, fn);
			if (OperatingSystem.IsMacOS())
				throw new InvalidOperationException($"{fn}: kern_return_t {rv}");
			if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
				throw new InvalidOperationException($"{fn}: errno {rv}");
			throw new InvalidOperationException($"{fn}: {rv}");
		}
	}

	static PreciseWait() {
		if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
			wrap("precisewait_init", precisewait_init());
		else
			throw new PlatformNotSupportedException("PreciseWait is only supported on Windows / MacOS / Linux / FreeBSD");
	}

	/// <summary>
	/// Suspends the calling thread for approximately <paramref name="ns"/> nanoseconds, subject to OS
	/// scheduling/etc. delays. May undershoot by up to 99ns on Windows before scheduling/etc. delays.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Currently behaves identically to <see cref="WaitPreferOvershoot(long)"/> on non-Windows platforms.
	/// </para>
	/// <para>
	/// On Windows, due to implementation constraints, precision is only to the nearest 100 nanoseconds;
	/// <paramref name="ns"/> is rounded down to a 100-nanosecond step.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="ns"/> is negative.
	/// </exception>
	public static void WaitPreferUndershoot(long ns) {
		ArgumentOutOfRangeException.ThrowIfNegative(ns);
		wrap("precisewait", precisewait(ns, false));
	}

	/// <summary>
	/// Suspends the calling thread for approximately <paramref name="ns"/> nanoseconds, subject to OS
	/// scheduling/etc. delays. May overshoot by up to 99ns on Windows before scheduling/etc. delays.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Currently behaves identically to <see cref="WaitPreferUndershoot(long)"/> on non-Windows platforms.
	/// </para>
	/// <para>
	/// On Windows, due to implementation constraints, precision is only to the nearest 100 nanoseconds;
	/// the <paramref name="ns"/> argument is rounded up to a 100-nanosecond step.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="ns"/> is negative.
	/// </exception>
	public static void WaitPreferOvershoot(long ns) {
		ArgumentOutOfRangeException.ThrowIfNegative(ns);
		wrap("precisewait", precisewait(ns, true));
	}
}
