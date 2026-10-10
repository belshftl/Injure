// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Injure.Gpu;

namespace AvaloniaEmbed;

/// <summary>
/// Renders offscreen, then copies each frame into a <see cref="WriteableBitmap"/> via readback;
/// there's one readback in flight at a time, so if the previous one is still being read, it skips
/// frames.
/// </summary>
public sealed class OffscreenBitmapOutput : IRenderOutput {
	private readonly GpuDevice device;
	private GpuTexture texture = null!;
	private GpuBuffer readback = null!;
	private uint pitch;

	public WriteableBitmap Bitmap { get; private set; } = null!;
	public event Action? FramePresented;

	public uint Width { get; private set; }
	public uint Height { get; private set; }
	public TextureFormat Format => TextureFormat.Rgba8Unorm;

	public OffscreenBitmapOutput(GpuDevice device, uint width, uint height) {
		this.device = device;
		allocate(width, height);
	}

	private void allocate(uint width, uint height) {
		Width = width;
		Height = height;
		pitch = (width * 4 + 255) / 256 * 256;
		texture = device.CreateTexture(new GpuTextureCreateParams(
			width, height, 1, 1, 1, TextureDimension.Dimension2d,
			Format, TextureUsage.RenderAttachment | TextureUsage.CopySrc
		));
		readback = device.CreateBuffer(pitch * height, BufferUsage.MapRead | BufferUsage.CopyDst);
		Bitmap = new WriteableBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
	}

	public void ResizeTo(uint width, uint height) {
		if (width == Width && height == Height)
			return;
		texture.Dispose();
		readback.Dispose();
		allocate(width, height);
	}

	public bool TryAcquire([NotNullWhen(true)] out IAcquiredOutput? output) {
		if (readback.MapState != BufferMapState.Unmapped) {
			output = null;
			return false;
		}
		output = new Image(this, texture, readback, Bitmap);
		return true;
	}

	public void Dispose() {
		texture.Dispose();
		readback.Dispose();
	}

	private sealed class Image(OffscreenBitmapOutput owner, GpuTexture texture, GpuBuffer readback, WriteableBitmap bitmap) : IAcquiredOutput {
		public GpuTextureViewRef View => texture.DefaultView;

		public void RecordFinalCommands(GpuCommandEncoderHandle encoder) =>
			encoder.CopyTextureToBuffer(
				texture, new GpuTextureRegion(0, 0, 0, texture.Width, texture.Height, 1, 0, TextureAspect.All),
				readback, new GpuTextureLayout(0, owner.pitch, texture.Height)
			);

		public void Present() =>
			readback.BeginMap(MapMode.Read, callback: status => {
				if (status != BufferMapStatus.Success)
					return;
				using (ILockedFramebuffer fb = bitmap.Lock()) {
					for (uint y = 0; y < texture.Height; y++) {
						unsafe {
							readback.ReadMapped(
								y * owner.pitch,
								new Span<byte>((byte*)fb.Address + y * fb.RowBytes, (int)(texture.Width * 4))
							);
						}
					}
				}
				readback.Unmap();
				owner.FramePresented?.Invoke();
			});

		public void Dispose() {
		}
	}
}
