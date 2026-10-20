// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// Common <see cref="BlendState"/>s.
/// </summary>
public static class BlendStates {
	/// <summary>
	/// Alpha blending for straight alpha: <c>src * src.a + dst * (1 - src.a)</c> for color, and
	/// <c>src.a + dst.a * (1 - src.a)</c> for alpha.
	/// </summary>
	public static readonly BlendState Alpha = new(
		Color: new(BlendOperation.Add, BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha),
		Alpha: new(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha)
	);

	/// <summary>
	/// Alpha blending for premultiplied alpha: <c>src + dst * (1 - src.a)</c> for color and alpha.
	/// </summary>
	public static readonly BlendState PremulAlpha = new(
		Color: new(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha),
		Alpha: new(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha)
	);

	/// <summary>
	/// Additive blending for straight alpha: <c>src * src.a + dst</c> for color, and
	/// <c>src.a + dst.a * (1 - src.a)</c> for alpha.
	/// </summary>
	public static readonly BlendState Additive = new(
		Color: new(BlendOperation.Add, BlendFactor.SrcAlpha, BlendFactor.One),
		Alpha: new(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha)
	);

	/// <summary>
	/// Additive blending for premultiplied alpha: <c>src + dst</c> for color, and
	/// <c>src.a + dst.a * (1 - src.a)</c> for alpha.
	/// </summary>
	public static readonly BlendState PremulAdditive = new(
		Color: new(BlendOperation.Add, BlendFactor.One, BlendFactor.One),
		Alpha: new(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha)
	);
}
