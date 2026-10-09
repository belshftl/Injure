// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Tests.Gpu;

// the SPIR-V fixtures are GLSL equivalents of GpuRig.Wgsl, compiled by ./Shaders/compile.sh
public sealed class GpuSpirvTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private static byte[] loadFixture(string name) {
		using Stream s = typeof(GpuSpirvTests).Assembly.GetManifestResourceStream($"spirv/{name}.spv")
			?? throw new InvalidOperationException($"missing SPIR-V fixture {name}.spv");
		byte[] bytes = new byte[s.Length];
		s.ReadExactly(bytes);
		return bytes;
	}

	private static uint[] words(byte[] bytes) {
		uint[] w = new uint[bytes.Length / 4];
		for (int i = 0; i < w.Length; i++)
			w[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
		return w;
	}

	private static byte[] byteSwapped(byte[] bytes) {
		byte[] swapped = (byte[])bytes.Clone();
		for (int i = 0; i < swapped.Length; i += 4)
			swapped.AsSpan(i, 4).Reverse();
		return swapped;
	}

	private byte[] render(GpuRig.Tinted tint, GpuRenderPipeline p) {
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using GpuBuffer vb = GpuRig.BufferWith(Device, BufferUsage.Vertex, GpuRig.Fullscreen().AsSpan());
		GpuRig.Run(Device, enc => {
			using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Clear(RawColor32.Black.ToRawF128()));
			pass.SetPipeline(p);
			pass.SetBindGroup(0, tint.Group, [tint.Stride]);
			pass.SetVertexBuffer(0, vb);
			pass.Draw(3);
		});
		return GpuRig.Read(Device, target);
	}

	[Fact]
	public void SpirvMatchesTheEquivalentWgsl() {
		using GpuRig.Tinted tint = new(Device, RawColor32.Red, new RawColor32(12, 34, 56, 255));
		// one module through each overload
		using GpuShaderModule vs = Device.CreateShaderModuleSpirv(loadFixture("tint.vert").AsSpan());
		using GpuShaderModule fs = Device.CreateShaderModuleSpirv(words(loadFixture("tint.frag")).AsSpan());
		using GpuRenderPipeline spirv = Device.CreateRenderPipeline(new GpuRenderPipelineCreateParams(
			tint.PipelineLayout,
			new VertexState(vs, "main", [GpuRig.Vertex3]),
			new FragmentState(fs, "main", [GpuRig.Opaque()])
		));
		using GpuRenderPipeline wgsl = tint.Pipeline(Device, [GpuRig.Opaque()]);

		byte[] fromSpirv = render(tint, spirv);
		Assert.Equal(new RawColor32(12, 34, 56, 255), GpuRig.At(fromSpirv, 4, 2, 2));
		Assert.Equal(render(tint, wgsl), fromSpirv);
	}

	[Fact]
	public void RejectsCodeThatIsntSpirv() {
		byte[] good = loadFixture("tint.frag");
		Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(ReadOnlySpan<byte>.Empty));
		Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(ReadOnlySpan<uint>.Empty));
		Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(good.AsSpan(0, good.Length - 2)));
		Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(good.AsSpan(0, 16)));
		Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(words(good).AsSpan(0, 4)));

		byte[] notSpirv = (byte[])good.Clone();
		notSpirv[0] ^= 0xff;
		ArgumentException e = Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(notSpirv.AsSpan()));
		Assert.Contains("isn't SPIR-V", e.Message);
		e = Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(words(notSpirv).AsSpan()));
		Assert.Contains("isn't SPIR-V", e.Message);
	}

	[Fact]
	public void ByteOverloadRejectsBigEndianWithADedicatedMessage() {
		byte[] bigEndian = byteSwapped(loadFixture("tint.frag"));
		ArgumentException e = Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(bigEndian.AsSpan()));
		Assert.Equal("code", e.ParamName);
		Assert.Contains("big-endian", e.Message);
	}

	[Fact]
	public void WordOverloadRejectsByteSwappedWords() {
		uint[] swapped = words(byteSwapped(loadFixture("tint.frag")));
		ArgumentException e = Assert.Throws<ArgumentException>(() => Device.CreateShaderModuleSpirv(swapped.AsSpan()));
		Assert.Contains("byte-swapped", e.Message);
	}
}
