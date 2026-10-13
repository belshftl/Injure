// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

// renders one mip level from the level above it, bound as `src`. each destination texel averages
// the source texels its footprint covers, weighted by overlap, which is a plain 2x2 box for even
// sizes and up to 3 texels per axis for odd sizes, so no source row or column gets dropped

@group(0) @binding(0)
var src: texture_2d<f32>;

// fullscreen triangle
@vertex
fn vs_main(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
	let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
	return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
}

fn srgbToLinear(c: vec3<f32>) -> vec3<f32> {
	return select(pow((c + 0.055) / 1.055, vec3<f32>(2.4)), c / 12.92, c <= vec3<f32>(0.04045));
}

fn linearToSrgb(c: vec3<f32>) -> vec3<f32> {
	return select(1.055 * pow(c, vec3<f32>(1.0 / 2.4)) - 0.055, c * 12.92, c <= vec3<f32>(0.0031308));
}

fn overlap(lo: f32, hi: f32, t: f32) -> f32 {
	return max(0.0, min(hi, t + 1.0) - max(lo, t));
}

struct Footprint {
	lo: vec2<f32>,
	hi: vec2<f32>,
	first: vec2<i32>,
	last: vec2<i32>,
};

fn footprint(pos: vec2<f32>) -> Footprint {
	let srcSize = vec2<f32>(textureDimensions(src));
	let dstSize = max(floor(srcSize / 2.0), vec2<f32>(1.0));
	let scale = srcSize / dstSize;
	let p = floor(pos);
	var f: Footprint;
	f.lo = p * scale;
	f.hi = (p + 1.0) * scale;
	f.first = vec2<i32>(floor(f.lo));
	// clamp because float error can push hi a little past the edge
	f.last = min(vec2<i32>(ceil(f.hi)) - 1, vec2<i32>(srcSize) - 1);
	return f;
}

@fragment
fn fs_raw(@builtin(position) pos: vec4<f32>) -> @location(0) vec4<f32> {
	let f = footprint(pos.xy);
	var sum = vec4<f32>(0.0);
	var total = 0.0;
	for (var y = f.first.y; y <= f.last.y; y++) {
		let wy = overlap(f.lo.y, f.hi.y, f32(y));
		for (var x = f.first.x; x <= f.last.x; x++) {
			let w = wy * overlap(f.lo.x, f.hi.x, f32(x));
			sum += w * textureLoad(src, vec2<i32>(x, y), 0);
			total += w;
		}
	}
	return sum / total;
}

// texels hold srgb color in straight alpha; average in linear light weighted by alpha so
// transparent texels don't bleed their (usually black) color into visible ones
// fully transparent footprints keep the unweighted average color instead of turning black
@fragment
fn fs_color(@builtin(position) pos: vec4<f32>) -> @location(0) vec4<f32> {
	let f = footprint(pos.xy);
	var premul = vec4<f32>(0.0);
	var plain = vec3<f32>(0.0);
	var total = 0.0;
	for (var y = f.first.y; y <= f.last.y; y++) {
		let wy = overlap(f.lo.y, f.hi.y, f32(y));
		for (var x = f.first.x; x <= f.last.x; x++) {
			let w = wy * overlap(f.lo.x, f.hi.x, f32(x));
			let t = textureLoad(src, vec2<i32>(x, y), 0);
			let lin = srgbToLinear(t.rgb);
			premul += w * vec4<f32>(lin * t.a, t.a);
			plain += w * lin;
			total += w;
		}
	}
	let a = premul.a / total;
	let rgb = select(plain / total, premul.rgb / premul.a, premul.a > 0.0);
	return vec4<f32>(linearToSrgb(rgb), a);
}
