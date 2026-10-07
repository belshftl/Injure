// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Hexa.NET.SDL3;

namespace Injure.Sdl;

/// <summary>
/// Thrown when an SDL call fails.
/// </summary>
public sealed class SdlException(string op, string message) : Exception($"{op}: {message}") {
	/// <summary>
	/// Name of the SDL function that failed, e.g. <c>SDL_CreateWindow</c>.
	/// </summary>
	public readonly string Operation = op;

	internal static SdlException FromLastError(string op) => new(op, SDL.GetErrorS());

	[StackTraceHidden]
	internal static void Check(bool v, [CallerArgumentExpression(nameof(v))] string? expr = null) {
		if (!v)
			throw new SdlException(getfnname(expr), SDL.GetErrorS());
	}

	private static string getfnname(string? expr) {
		if (string.IsNullOrWhiteSpace(expr))
			return "<unknown SDL call>";
		expr = expr.Replace("SDL.", "SDL_");
		int paren = expr.IndexOf('(');
		return paren >= 0 ? expr[..paren].Trim() : expr.Trim();
	}
}
