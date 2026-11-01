// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Draw.PixelConv;

/// <summary>
/// Transfer function of stored color channel values, used by <see cref="AlphaPremultiplier"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TransferFunction {
	/// <summary>Raw switch tag for <see cref="TransferFunction"/>.</summary>
	public enum Case {
		/// <summary>
		/// Values are proportional to linear light.
		/// </summary>
		Linear = 1,

		/// <summary>
		/// Values are encoded with the sRGB transfer function.
		/// </summary>
		Srgb,
	}
}
