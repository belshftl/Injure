// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.CompilerServices;
using FreeTypeSharp;

namespace Injure.Draw.Text;

/// <summary>
/// Thrown if a FreeType call fails.
/// </summary>
public sealed class FtException : Exception {
	internal FtException(string function, FT_Error error) : this(function, errorName(error)) {}

	private FtException(string function, string error) : base($"{function}: {error}") {
		Function = function;
		Error = error;
	}

	/// <summary>
	/// The FreeType function that failed, e.g. <c>FT_New_Memory_Face</c>.
	/// </summary>
	public string Function { get; }

	/// <summary>
	/// The error FreeType returned, either as the name of its error code, e.g.
	/// <c>FT_Err_Unknown_File_Format</c>, or its numeric value in hexadecimal, e.g. <c>0x55</c>, if
	/// the code has no name.
	/// </summary>
	public string Error { get; }

	private static string errorName(FT_Error error) => Enum.GetName(error) ?? $"0x{(int)error:X}";

	[StackTraceHidden]
	internal static void Check(FT_Error result, [CallerArgumentExpression(nameof(result))] string? expr = null) {
		if (result == 0)
			return;
		throw new FtException(functionName(expr), result);
	}

	private static string functionName(string? expr) {
		if (string.IsNullOrWhiteSpace(expr))
			return "<unknown FT call>";
		int paren = expr.IndexOf('(');
		return paren >= 0 ? expr[..paren].Trim() : expr.Trim();
	}
}
