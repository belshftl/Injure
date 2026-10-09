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
	public static readonly BlendState Alpha = new() {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.SrcAlpha,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
	};

	/// <summary>
	/// Alpha blending for premultiplied alpha: <c>src + dst * (1 - src.a)</c> for color and alpha.
	/// </summary>
	public static readonly BlendState PremulAlpha = new() {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
	};

	/// <summary>
	/// Additive blending for straight alpha: <c>src * src.a + dst</c> for color, and
	/// <c>src.a + dst.a * (1 - src.a)</c> for alpha.
	/// </summary>
	public static readonly BlendState Additive = new() {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.SrcAlpha,
			DstFactor = BlendFactor.One,
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
	};

	/// <summary>
	/// Additive blending for premultiplied alpha: <c>src + dst</c> for color, and
	/// <c>src.a + dst.a * (1 - src.a)</c> for alpha.
	/// </summary>
	public static readonly BlendState PremulAdditive = new() {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.One,
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
	};
}
