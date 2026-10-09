// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Injure.Gpu;

/// <summary>
/// Thrown if a WebGPU call fails.
/// </summary>
/// <param name="op">
/// The failed WebGPU call or operation, e.g. <c>wgpuDeviceCreateBuffer</c>.
/// </param>
/// <param name="message">What went wrong.</param>
public sealed class WebgpuException(string op, string message) : Exception($"{op}: {message}") {
	/// <summary>
	/// The failed WebGPU call or operation, e.g. <c>wgpuDeviceCreateBuffer</c>.
	/// </summary>
	public readonly string Operation = op;

	/// <summary>
	/// Returns <paramref name="v"/> if it isn't the <see langword="default"/> (null) value, and
	/// throws otherwise.
	/// </summary>
	/// <typeparam name="T">Handle type returned by the call.</typeparam>
	/// <param name="v">Value returned by a WebGPU call.</param>
	/// <param name="expr">Filled in by the compiler; used to name the call in the exception.</param>
	/// <exception cref="WebgpuException">
	/// Thrown if <paramref name="v"/> is the <see langword="default"/> value.
	/// </exception>
	/// <remarks>
	/// Intended for code that calls the WebGPU bindings directly, e.g. through
	/// <c>DangerousGetNative</c> handles.
	/// </remarks>
	[StackTraceHidden]
	public static T Check<T>(T v, [CallerArgumentExpression(nameof(v))] string? expr = null) where T : unmanaged, IEquatable<T> {
		if (!v.Equals(default))
			return v;
		throw new WebgpuException(getfnname(expr), "WebGPU call returned null");
	}

	private static string getfnname(string? expr) {
		if (string.IsNullOrWhiteSpace(expr))
			return "<unknown WebGPU call>";
		int paren = expr.IndexOf('(');
		if (paren < 0)
			return expr.Trim();
		ReadOnlySpan<char> head = expr.AsSpan(0, paren).Trim();
		int dot = head.LastIndexOf('.');
		return dot >= 0 ? head[(dot + 1)..].ToString() : head.ToString();
	}
}
