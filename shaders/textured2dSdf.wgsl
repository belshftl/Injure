// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

// unused until the material rework brings this back as a raw texture material; its CanvasUniform
// still has the sdf fields, which the canvas no longer fills in

// the sdf fields are only read by us and not the other shaders; outlineColor is premultiplied
// linear
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

fn srgbToLinear(c: vec3<f32>) -> vec3<f32> {
	return select(pow((c + 0.055) / 1.055, vec3<f32>(2.4)), c / 12.92, c <= vec3<f32>(0.04045));
}

// vertex colors come in as straight srgb; everything after this is premultiplied linear
@vertex
fn vs_main(v: VsIn) -> VsOut {
	var out: VsOut;
	out.pos = u.proj * vec4<f32>(v.pos, 0.0, 1.0);
	out.uv = v.uv;
	out.color = vec4<f32>(srgbToLinear(v.color.rgb) * v.color.a, v.color.a);
	return out;
}

@fragment
fn fs_main(v: VsOut) -> @location(0) vec4<f32> {
	let sz = vec2<f32>(textureDimensions(tex));
	let range = vec2<f32>(u.distanceRangeTexels) / sz;
	let pxPerUv = 1.0 / fwidth(v.uv);
	let pxrange = max(0.5 * dot(range, pxPerUv), 1.0);

	let d = textureSample(tex, smp, v.uv).r;
	let sd = pxrange * (d - u.edgeValue);
	let aa = max(u.softnessPixels, 1e-6);

	// coverage of the fill, and of the ring of outline around it; the two cover disjoint parts of
	// the pixel, so the premultiplied colors weighted by their coverage just add up
	let fillA = smoothstep(-aa, aa, sd);
	let outlineA = smoothstep(-aa, aa, sd + u.outlineWidthPixels) - fillA;
	return v.color * fillA + u.outlineColor * outlineA;
}
