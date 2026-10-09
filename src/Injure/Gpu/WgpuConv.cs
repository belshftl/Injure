// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

internal static class WgpuConv {
	public static WGPUAddressMode ToWebgpuType(this AddressMode v) => (WGPUAddressMode)v.Tag;
	public static WGPUBackendType ToWebgpuType(this BackendType v) => (WGPUBackendType)v.Tag;
	public static WGPUBlendFactor ToWebgpuType(this BlendFactor v) => (WGPUBlendFactor)v.Tag;
	public static WGPUBlendOperation ToWebgpuType(this BlendOperation v) => (WGPUBlendOperation)v.Tag;
	public static WGPUBufferBindingType ToWebgpuType(this BufferBindingType v) => (WGPUBufferBindingType)v.Tag;
	public static WGPUBufferUsage ToWebgpuType(this BufferUsage v) => (WGPUBufferUsage)v.Mask;
	public static WGPUColorWriteMask ToWebgpuType(this ColorWriteMask v) => (WGPUColorWriteMask)v.Mask;
	public static WGPUCompareFunction ToWebgpuType(this CompareFunction v) => (WGPUCompareFunction)v.Tag;
	public static WGPUCullMode ToWebgpuType(this CullMode v) => (WGPUCullMode)v.Tag;
	public static WGPUFilterMode ToWebgpuType(this FilterMode v) => (WGPUFilterMode)v.Tag;
	public static WGPUFrontFace ToWebgpuType(this FrontFace v) => (WGPUFrontFace)v.Tag;
	public static WGPUIndexFormat ToWebgpuType(this IndexFormat v) => (WGPUIndexFormat)v.Tag;
	public static WGPULoadOp ToWebgpuType(this LoadOp v) => (WGPULoadOp)v.Tag;
	public static WGPUMipmapFilterMode ToWebgpuType(this MipmapFilterMode v) => (WGPUMipmapFilterMode)v.Tag;
	public static WGPUPowerPreference ToWebgpuType(this PowerPreference v) => (WGPUPowerPreference)v.Tag;
	public static WGPUPrimitiveTopology ToWebgpuType(this PrimitiveTopology v) => (WGPUPrimitiveTopology)v.Tag;
	public static WGPUSamplerBindingType ToWebgpuType(this SamplerBindingType v) => (WGPUSamplerBindingType)v.Tag;
	public static WGPUShaderStage ToWebgpuType(this ShaderStage v) => (WGPUShaderStage)v.Mask;
	public static WGPUStencilOperation ToWebgpuType(this StencilOperation v) => (WGPUStencilOperation)v.Tag;
	public static WGPUStorageTextureAccess ToWebgpuType(this StorageTextureAccess v) => (WGPUStorageTextureAccess)v.Tag;
	public static WGPUStoreOp ToWebgpuType(this StoreOp v) => (WGPUStoreOp)v.Tag;
	public static WGPUTextureAspect ToWebgpuType(this TextureAspect v) => (WGPUTextureAspect)v.Tag;
	public static WGPUTextureDimension ToWebgpuType(this TextureDimension v) => (WGPUTextureDimension)v.Tag;
	public static WGPUTextureFormat ToWebgpuType(this TextureFormat v) => (WGPUTextureFormat)v.Tag;
	public static WGPUTextureSampleType ToWebgpuType(this TextureSampleType v) => (WGPUTextureSampleType)v.Tag;
	public static WGPUTextureUsage ToWebgpuType(this TextureUsage v) => (WGPUTextureUsage)v.Mask;
	public static WGPUTextureViewDimension ToWebgpuType(this TextureViewDimension v) => (WGPUTextureViewDimension)v.Tag;
	public static WGPUVertexFormat ToWebgpuType(this VertexFormat v) => (WGPUVertexFormat)v.Tag;
	public static WGPUVertexStepMode ToWebgpuType(this VertexStepMode v) => (WGPUVertexStepMode)v.Tag;

	public static TextureAspect FromWebgpuType(this WGPUTextureAspect v) => TextureAspect.Enum.FromMirror(v);
	public static TextureFormat FromWebgpuType(this WGPUTextureFormat v) => TextureFormat.Enum.FromMirror(v);
	public static TextureUsage FromWebgpuType(this WGPUTextureUsage v) => TextureUsage.Flags.FromMirror(v);
	public static TextureViewDimension FromWebgpuType(this WGPUTextureViewDimension v) => TextureViewDimension.Enum.FromMirror(v);

	public static BufferMapStatus FromWebgpuType(this WGPUMapAsyncStatus v) => v switch {
		WGPUMapAsyncStatus.Success => BufferMapStatus.Success,
		WGPUMapAsyncStatus.Aborted => BufferMapStatus.Aborted,
		_ => BufferMapStatus.Error,
	};
}
