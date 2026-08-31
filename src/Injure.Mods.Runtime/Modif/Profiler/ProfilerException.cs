// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// Exception thrown if a profiler call fails.
/// </summary>
public sealed class ProfilerException : Exception {
	/// <summary>
	/// The <c>HRESULT</c> the profiler reported.
	/// </summary>
	public int Hresult { get; }

	internal ProfilerException(string what, int hresult) : base($"{what} failed with hresult 0x{hresult:x8}") => Hresult = hresult;
}
