// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Primitives;

/// <summary>
/// 32bpp RGBA color with no color space information. Used for byte data where the meaning is
/// defined by the API that produced it, e.g. texture readback.
/// </summary>
[Color32Type]
public readonly partial struct RawColor32 {
	/// <summary>
	/// Reinterprets this value as if it is sRGB-encoded, without changing the bytes.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref readonly SrgbColor32 AssumeSrgb() =>
		// tests with Godbolt confirm this is a real optimization:
		// - on CoreCLR, a byte-by-byte copy via shl/or turns into `mov eax, dword ptr [rdi]; ret`
		// - on NativeAOT, movzx-ing the four args byte-by-byte into rdi/rsi/rdx/rcx + a
		//   `call SrgbColor32:.ctor` turns into the same `mov eax, dword ptr [rax]`
		// it's certainly interesting that CoreCLR manages to elide the call but not the byte-by-byte copy
		ref Unsafe.As<RawColor32, SrgbColor32>(ref Unsafe.AsRef(in this));

	/// <summary>
	/// Reinterprets the span as if all of its values are sRGB-encoded, without changing the bytes.
	/// </summary>
	public static ReadOnlySpan<SrgbColor32> SpanAssumeSrgb(ReadOnlySpan<RawColor32> span) =>
		MemoryMarshal.Cast<RawColor32, SrgbColor32>(span);

	/// <summary>
	/// Reinterprets the span as if all of its values are sRGB-encoded, without changing the bytes.
	/// </summary>
	public static Span<SrgbColor32> SpanAssumeSrgb(Span<RawColor32> span) =>
		MemoryMarshal.Cast<RawColor32, SrgbColor32>(span);

	/// <summary>
	/// Normalizes this value to a <see cref="RawColorF128"/>, with each channel mapped from [0, 255]
	/// to [0, 1].
	/// </summary>
	public RawColorF128 ToRawF128() => new(R / 255f, G / 255f, B / 255f, A / 255f);
}
