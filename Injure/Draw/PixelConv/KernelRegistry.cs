// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Draw.PixelConv;

using unsafe Kernel = delegate *<ref readonly PixelConversionPlan, byte*, byte*, nuint, void>;

internal readonly unsafe struct KernelFallbackChain(Kernel avx2, Kernel ssse3, Kernel sse2, Kernel advSimd, Kernel scalar, bool sentinel = false) {
	public readonly Kernel Avx2 = avx2;
	public readonly Kernel Ssse3 = ssse3;
	public readonly Kernel Sse2 = sse2;
	public readonly Kernel AdvSimd = advSimd;
	public readonly Kernel Scalar = scalar;
	public readonly bool Sentinel = sentinel;

	public Kernel Pick(out PlanBackend chosen) {
		if (Sentinel)
			throw new InternalStateException("this KernelFallbackChain is a sentinel value");
		if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && Avx2 is not null) {
			chosen = PlanBackend.Avx2;
			return Avx2;
		}
		if (System.Runtime.Intrinsics.X86.Ssse3.IsSupported && Ssse3 is not null) {
			chosen = PlanBackend.Ssse3;
			return Ssse3;
		}
		if (System.Runtime.Intrinsics.X86.Sse2.IsSupported && Sse2 is not null) {
			chosen = PlanBackend.Sse2;
			return Sse2;
		}
		if (System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported && AdvSimd is not null) {
			chosen = PlanBackend.AdvSimd;
			return AdvSimd;
		}
		if (Scalar is not null) {
			chosen = PlanBackend.Scalar;
			return Scalar;
		}
		throw new InternalStateException("this KernelFallbackChain is empty");
	}

	public bool TryPickSpecific(PlanBackend selected, out Kernel kernel) {
		kernel = null;
		switch (selected.Tag) {
		case PlanBackend.Case.Avx2:
			if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && Avx2 is not null) {
				kernel = Avx2;
				return true;
			}
			return false;
		case PlanBackend.Case.Ssse3:
			if (System.Runtime.Intrinsics.X86.Ssse3.IsSupported && Ssse3 is not null) {
				kernel = Ssse3;
				return true;
			}
			return false;
		case PlanBackend.Case.Sse2:
			if (System.Runtime.Intrinsics.X86.Sse2.IsSupported && Sse2 is not null) {
				kernel = Sse2;
				return true;
			}
			return false;
		case PlanBackend.Case.AdvSimd:
			if (System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported && AdvSimd is not null) {
				kernel = AdvSimd;
				return true;
			}
			return false;
		case PlanBackend.Case.Scalar:
			if (Scalar is not null) {
				kernel = Scalar;
				return true;
			}
			return false;
		default:
			throw new InvalidOperationException("can't select this plan backend");
		}
	}
}

internal static unsafe class KernelRegistry {
	public static readonly ImmutableArray<KernelFallbackChain> Kernels = [
		new(null, null, null, null, null, sentinel: true), // Memcpy
		new(&Avx2Kernels.Copy32SetAlpha, null, &Sse2Kernels.Copy32SetAlpha, &AdvSimdKernels.Copy32SetAlpha, &ScalarKernels.Copy32SetAlpha),
		new(&Avx2Kernels.Copy64SetAlpha, null, &Sse2Kernels.Copy64SetAlpha, &AdvSimdKernels.Copy64SetAlpha, &ScalarKernels.Copy64SetAlpha),

		new(&Avx2Kernels.Shuffle32, &Ssse3Kernels.Shuffle32, null, &AdvSimdKernels.Shuffle32, &ScalarKernels.Shuffle32),
		new(&Avx2Kernels.Expand24To32, &Ssse3Kernels.Expand24To32, null, &AdvSimdKernels.Expand24To32, &ScalarKernels.Expand24To32),
		new(&Avx2Kernels.Contract32To24, null, null, null, &ScalarKernels.Contract32To24),

		new(null, null, null, null, &ScalarKernels.Shuffle64),
		new(null, null, null, null, &ScalarKernels.Widen32To64),
		new(null, null, null, null, &ScalarKernels.Narrow64To32),

		new(null, null, null, null, &ScalarKernels.Packed16To32),
		new(null, null, null, null, &ScalarKernels.Unpacked32ToPacked16),

		new(null, null, null, null, &ScalarKernels.Generic),
	];
}
