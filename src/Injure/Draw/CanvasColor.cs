// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Primitives;

namespace Injure.Draw;

internal static class CanvasColor {
	public static RawColorF128 ToLinearPremul(SrgbStraight32 c) {
		float a = c.A / 255f;
		return new RawColorF128(
			SrgbTransfer.Decode8(c.R) * a,
			SrgbTransfer.Decode8(c.G) * a,
			SrgbTransfer.Decode8(c.B) * a,
			a
		);
	}

	public static SrgbStraight32 MultiplyLinear(SrgbStraight32 x, SrgbStraight32 y) => new(
		SrgbTransfer.Encode8(SrgbTransfer.Decode8(x.R) * SrgbTransfer.Decode8(y.R)),
		SrgbTransfer.Encode8(SrgbTransfer.Decode8(x.G) * SrgbTransfer.Decode8(y.G)),
		SrgbTransfer.Encode8(SrgbTransfer.Decode8(x.B) * SrgbTransfer.Decode8(y.B)),
		(byte)((x.A * y.A + 127) / 255)
	);
}
