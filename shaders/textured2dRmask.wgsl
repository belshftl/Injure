// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

// the sdf fields are only read by the sdf shader
struct CanvasUniform {
	proj: mat4x4<f32>,
	distanceRangeTexels: f32,
	edgeValue: f32,
	softnessPixels: f32,
	outlineWidthPixels: f32,
	outlineColor: vec4<f32>
};

@group(0) @binding(0)
var<uniform> u: CanvasUniform;

@group(1) @binding(0)
var tex: texture_2d<f32>;
@group(2) @binding(0)
var smp: sampler;

struct VsIn {
	@location(0) pos: vec2<f32>,
	@location(1) uv: vec2<f32>,
	@location(2) color: vec4<f32>
};

struct VsOut {
	@builtin(position) pos: vec4<f32>,
	@location(0) uv: vec2<f32>,
	@location(1) color: vec4<f32>
};

@vertex
fn vs_main(v: VsIn) -> VsOut {
	var out: VsOut;
	out.pos = u.proj * vec4<f32>(v.pos, 0.0, 1.0);
	out.uv = v.uv;
	out.color = v.color;
	return out;
}

@fragment
fn fs_main(v: VsOut) -> @location(0) vec4<f32> {
	return vec4(v.color.rgb, v.color.a * textureSample(tex, smp, v.uv).r);
}

// moves uv so the linear sampler's blend between two texels spans one destination pixel centered on
// their shared edge, which makes the blend weights the texels' coverage of the pixel; at a scale of
// 1 or below, the uv is unchanged
fn areaWeightedUv(uv: vec2<f32>) -> vec2<f32> {
	let size = vec2<f32>(textureDimensions(tex));
	let texel = uv * size;
	// destination pixels per texel along each texture axis; the max on fwidth keeps a constant uv
	// (e.g. untextured draws) from making this infinite and the uv nan
	let scale = max(1.0 / max(fwidth(texel), vec2(1e-6)), vec2(1.0));
	let center = floor(texel) + 0.5;
	let d = texel - center;
	let plateau = 0.5 - 0.5 / scale;
	return (center + (d - clamp(d, -plateau, plateau)) * scale) / size;
}

// mip selection uses the unadjusted uv's gradients, the same as fs_main's
@fragment
fn fs_area_weighted(v: VsOut) -> @location(0) vec4<f32> {
	return vec4(v.color.rgb, v.color.a * textureSampleGrad(tex, smp, areaWeightedUv(v.uv), dpdx(v.uv), dpdy(v.uv)).r);
}
