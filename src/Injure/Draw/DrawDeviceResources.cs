// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;

namespace Injure.Draw;

internal sealed class DrawDeviceResources : IDisposable {
	private readonly GpuDevice device;

	// vertex-visible uniform buffer at binding 0 holding a GlobalsUniform
	public readonly GpuBindGroupLayout GlobalsUniformLayout;
	// "float"-sampled 2D color texture view at binding 0 + filtering sampler at binding 1
	public readonly GpuBindGroupLayout ColorTexture2dLayout;
	// "depth"-sampled 2D depth texture view at binding 0 + filtering sampler at binding 1
	public readonly GpuBindGroupLayout FilteringDepthTexture2dLayout;
	// "depth"-sampled 2D depth texture view at binding 0 + comparison sampler at binding 1
	public readonly GpuBindGroupLayout ComparisonDepthTexture2dLayout;

	public static DrawDeviceResources For(GpuDevice device) => device.GetOrAttach(static d => new DrawDeviceResources(d));

	private DrawDeviceResources(GpuDevice device) {
		this.device = device;
		GlobalsUniformLayout = device.CreateBindGroupLayout(
			[
				new GpuBindGroupLayoutEntry(
					Binding: 0,
					Visibility: ShaderStage.Vertex,
					Layout: new GpuBufferBindingLayout(
						Type: BufferBindingType.Uniform,
						MinBindingSize: (ulong)GlobalsUniform.Size
					)
				),
			]
		);

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

		FilteringDepthTexture2dLayout = device.CreateBindGroupLayout(
			[
				new GpuBindGroupLayoutEntry(
					Binding: 0,
					Visibility: ShaderStage.Fragment,
					Layout: new GpuTextureBindingLayout(
						SampleType: TextureSampleType.Depth,
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

		ComparisonDepthTexture2dLayout = device.CreateBindGroupLayout(
			[
				new GpuBindGroupLayoutEntry(
					Binding: 0,
					Visibility: ShaderStage.Fragment,
					Layout: new GpuTextureBindingLayout(
						SampleType: TextureSampleType.Depth,
						ViewDimension: TextureViewDimension.Dimension2d,
						Multisampled: false
					)
				),
				new GpuBindGroupLayoutEntry(
					Binding: 1,
					Visibility: ShaderStage.Fragment,
					Layout: new GpuSamplerBindingLayout(
						Type: SamplerBindingType.Comparison
					)
				),
			]
		);
	}

	/// <summary>
	/// Creates a texture+sampler bind group for a 2D color texture view and a filtering sampler,
	/// returning an owning object.
	/// </summary>
	/// <param name="view">2D non-multisampled color view to sample.</param>
	/// <param name="sampler">Filtering sampler to pair with the view.</param>
	/// <remarks>
	/// <para>
	/// Convenience wrapper over <see cref="GpuDevice.CreateBindGroup(GpuBindGroupLayoutHandle, ReadOnlySpan{GpuBindGroupEntry})"/>.
	/// The returned bind group matches <see cref="ColorTexture2dLayout"/>.
	/// </para>
	/// <para>
	/// No attempt to check if the view's color format's sample type is <c>"float"</c> or to check if
	/// the sampler is a filtering sampler is made, so if it isn't, a WebGPU validation error will most
	/// likely occur.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="view"/> or <paramref name="sampler"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="view"/> is not a <see cref="TextureUsage.TextureBinding"/>-enabled,
	/// 2D, non-multisampled, color view.
	/// </exception>
	public GpuBindGroup CreateColorTexture2dBindGroup(GpuTextureViewHandle view, GpuSamplerHandle sampler) {
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if (view.Usage.HasNone(TextureUsage.TextureBinding))
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2d)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (view.Format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float
			or TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8 or TextureFormat.Case.Stencil8)
			throw new ArgumentException("view must be a color format", nameof(view));
		return device.CreateBindGroup(
			ColorTexture2dLayout,
			[
				new GpuBindGroupEntry(Binding: 0, new GpuTextureViewBindingResource(view)),
				new GpuBindGroupEntry(Binding: 1, new GpuSamplerBindingResource(sampler)),
			]
		);
	}

	/// <summary>
	/// Creates a texture+sampler bind group for a texture's default view, assuming it is a 2D color
	/// view, and a filtering sampler, returning an owning object.
	/// </summary>
	/// <param name="texture">Texture whose default view will be used.</param>
	/// <param name="sampler">Filtering sampler to pair with the view.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="texture"/> or <paramref name="sampler"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="texture"/>'s <see cref="GpuTextureHandle.DefaultView"/> is
	/// not a <see cref="TextureUsage.TextureBinding"/>-enabled, 2D, non-multisampled, color view.
	/// </exception>
	/// <inheritdoc cref="CreateColorTexture2dBindGroup(GpuTextureViewHandle, GpuSamplerHandle)"/>
	public GpuBindGroup CreateColorTexture2dBindGroup(GpuTextureHandle texture, GpuSamplerHandle sampler) {
		ArgumentNullException.ThrowIfNull(texture);
		return CreateColorTexture2dBindGroup(texture.DefaultView, sampler);
	}

	/// <summary>
	/// Creates a texture+sampler bind group for a 2D depth-only texture view and a filtering sampler,
	/// returning an owning object.
	/// </summary>
	/// <param name="view">2D non-multisampled depth-only view to sample.</param>
	/// <param name="sampler">Filtering sampler to pair with the view.</param>
	/// <remarks>
	/// <para>
	/// Convenience wrapper over <see cref="GpuDevice.CreateBindGroup(GpuBindGroupLayoutHandle, ReadOnlySpan{GpuBindGroupEntry})"/>.
	/// The returned bind group matches <see cref="FilteringDepthTexture2dLayout"/>.
	/// </para>
	/// <para>
	/// No attempt to check if the view's color format's sample type is <c>"depth"</c> or to check if
	/// the sampler is a filtering sampler is made, so if it isn't, a WebGPU validation error will most
	/// likely occur.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="view"/> or <paramref name="sampler"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="view"/> is not a <see cref="TextureUsage.TextureBinding"/>-enabled,
	/// 2D, non-multisampled, depth-only view.
	/// </exception>
	public GpuBindGroup CreateFilteringDepthTexture2dBindGroup(GpuTextureViewHandle view, GpuSamplerHandle sampler) {
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if (view.Usage.HasNone(TextureUsage.TextureBinding))
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2d)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (!(view.Format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float))
			throw new ArgumentException("view must be a depth-only format", nameof(view));
		return device.CreateBindGroup(
			FilteringDepthTexture2dLayout,
			[
				new GpuBindGroupEntry(Binding: 0, new GpuTextureViewBindingResource(view)),
				new GpuBindGroupEntry(Binding: 1, new GpuSamplerBindingResource(sampler)),
			]
		);
	}

	/// <summary>
	/// Creates a texture+sampler bind group for a 2D depth-only texture view and a comparison sampler,
	/// returning an owning object.
	/// </summary>
	/// <param name="view">2D non-multisampled depth-only view to sample.</param>
	/// <param name="sampler">Comparison sampler to pair with the view.</param>
	/// <remarks>
	/// <para>
	/// Convenience wrapper over <see cref="GpuDevice.CreateBindGroup(GpuBindGroupLayoutHandle, ReadOnlySpan{GpuBindGroupEntry})"/>.
	/// The returned bind group matches <see cref="ComparisonDepthTexture2dLayout"/>.
	/// </para>
	/// <para>
	/// No attempt to check if the view's color format's sample type is <c>"depth"</c> or to check if
	/// the sampler is a comparison sampler is made, so if it isn't, a WebGPU validation error will
	/// most likely occur.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="view"/> or <paramref name="sampler"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="view"/> is not a <see cref="TextureUsage.TextureBinding"/>-enabled,
	/// 2D, non-multisampled, depth-only view.
	/// </exception>
	public GpuBindGroup CreateComparisonDepthTexture2dBindGroup(GpuTextureViewHandle view, GpuSamplerHandle sampler) {
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if (view.Usage.HasNone(TextureUsage.TextureBinding))
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2d)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (!(view.Format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float))
			throw new ArgumentException("view must be a depth-only format", nameof(view));
		return device.CreateBindGroup(
			ComparisonDepthTexture2dLayout,
			[
				new GpuBindGroupEntry(Binding: 0, new GpuTextureViewBindingResource(view)),
				new GpuBindGroupEntry(Binding: 1, new GpuSamplerBindingResource(sampler)),
			]
		);
	}

	public void Dispose() {
		ComparisonDepthTexture2dLayout.Dispose();
		FilteringDepthTexture2dLayout.Dispose();
		ColorTexture2dLayout.Dispose();
		GlobalsUniformLayout.Dispose();
	}
}
