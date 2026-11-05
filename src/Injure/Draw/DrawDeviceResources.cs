// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// Bind group layouts shared by the texture types, <see cref="SrgbRenderTarget2d"/>, and
/// <see cref="Canvas"/>, and helpers creating their bind groups. Attached to the device, so there's
/// one per <see cref="GpuDevice"/>; obtained with <see cref="For(GpuDevice)"/>.
/// </summary>
internal sealed class DrawDeviceResources : IDisposable {
	private readonly GpuDevice device;

	/// <summary>
	/// Binding 0: a <c>"float"</c>-sampled 2D color texture view; binding 1: a filtering sampler.
	/// </summary>
	public readonly GpuBindGroupLayout ColorTexture2dLayout;

	/// <summary>
	/// Binding 0: a <c>"float"</c>-sampled 2D color texture view. <see cref="Canvas"/> binds the
	/// sampler separately.
	/// </summary>
	public readonly GpuBindGroupLayout ColorTexture2dViewLayout;

	public static DrawDeviceResources For(GpuDevice device) => device.GetOrAttach(static d => new DrawDeviceResources(d));

	private DrawDeviceResources(GpuDevice device) {
		this.device = device;
		ColorTexture2dLayout = device.CreateBindGroupLayout(
			[
				new GpuBindGroupLayoutEntry(
					Binding: 0,
					Visibility: ShaderStage.Fragment,
					Layout: new GpuTextureBindingLayout(
						SampleType: TextureSampleType.Float,
						ViewDimension: TextureViewDimension.Dimension2d,
						Multisampled: false
					)
				),
				new GpuBindGroupLayoutEntry(
					Binding: 1,
					Visibility: ShaderStage.Fragment,
					Layout: new GpuSamplerBindingLayout(
						Type: SamplerBindingType.Filtering
					)
				),
			]
		);

		ColorTexture2dViewLayout = device.CreateBindGroupLayout(
			[
				new GpuBindGroupLayoutEntry(
					Binding: 0,
					Visibility: ShaderStage.Fragment,
					Layout: new GpuTextureBindingLayout(
						SampleType: TextureSampleType.Float,
						ViewDimension: TextureViewDimension.Dimension2d,
						Multisampled: false
					)
				),
			]
		);

	}

	/// <summary>
	/// Creates a bind group of <paramref name="view"/> and <paramref name="sampler"/> for
	/// <see cref="ColorTexture2dLayout"/>, owned by the caller.
	/// </summary>
	/// <param name="view">
	/// Must be a <see cref="TextureUsage.TextureBinding"/>-usable, 2D, single-sampled color view.
	/// </param>
	/// <param name="sampler">Must be a filtering sampler.</param>
	public GpuBindGroup CreateColorTexture2dBindGroup(GpuTextureViewHandle view, GpuSamplerHandle sampler) {
		chkColorView(view);
		return createTextureBindGroup(ColorTexture2dLayout, view, sampler);
	}

	/// <summary>
	/// Creates a bind group of <paramref name="view"/> for <see cref="ColorTexture2dViewLayout"/>,
	/// owned by the caller.
	/// </summary>
	/// <param name="view">
	/// Must be a <see cref="TextureUsage.TextureBinding"/>-usable, 2D, single-sampled color view.
	/// </param>
	public GpuBindGroup CreateColorTexture2dViewBindGroup(GpuTextureViewHandle view) {
		chkColorView(view);
		return device.CreateBindGroup(ColorTexture2dViewLayout, [new GpuBindGroupEntry(Binding: 0, new GpuTextureViewBindingResource(view))]);
	}

	private GpuBindGroup createTextureBindGroup(GpuBindGroupLayout layout, GpuTextureViewHandle view, GpuSamplerHandle sampler) =>
		device.CreateBindGroup(
			layout,
			[
				new GpuBindGroupEntry(Binding: 0, new GpuTextureViewBindingResource(view)),
				new GpuBindGroupEntry(Binding: 1, new GpuSamplerBindingResource(sampler)),
			]
		);

	private static void chkColorView(GpuTextureViewHandle view) {
		if (view.Format.IsDepthOrStencil())
			throw new InternalStateException($"color bind group requested for depth/stencil view format {view.Format}");
	}

	public void Dispose() {
		ColorTexture2dViewLayout.Dispose();
		ColorTexture2dLayout.Dispose();
	}
}
