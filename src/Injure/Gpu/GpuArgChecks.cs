// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Gpu;

internal static class GpuArgChecks {
	[StackTraceHidden]
	public static void Field<T>(T value, string paramName, string path) where T : struct, IHasInvalidDefault {
		if (value.IsDefault)
			Throw<T>($"{paramName}.{path}");
	}

	[StackTraceHidden]
	public static void Field<T>(T? value, string paramName, string path) where T : struct, IHasInvalidDefault {
		if (value is T v && v.IsDefault)
			Throw<T>($"{paramName}.{path}");
	}

	public static bool IsDefault<T>(T value) where T : struct, IHasInvalidDefault => value.IsDefault;

	[DoesNotReturn]
	[StackTraceHidden]
	public static void Throw<T>(string paramName) where T : struct, IHasInvalidDefault =>
		throw ArgumentInvalidDefaultException.For<T>(paramName);
}
