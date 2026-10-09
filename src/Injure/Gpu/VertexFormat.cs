// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The format of a vertex attribute in a vertex buffer, and the type the shader reads it as.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>, which is invalid for
/// vertex attributes.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUVertexFormat))]
public readonly partial struct VertexFormat {
	/// <summary>Raw switch tag for <see cref="VertexFormat"/>.</summary>
	public enum Case {
		/// <summary>
		/// No format. Invalid for vertex attributes.
		/// </summary>
		None = 0,

		/// <summary>
		/// One 8-bit unsigned integer component; read as <c>u32</c>.
		/// </summary>
		Uint8 = 1,

		/// <summary>
		/// Two 8-bit unsigned integer components; read as <c>vec2u</c>.
		/// </summary>
		Uint8x2 = 2,

		/// <summary>
		/// Four 8-bit unsigned integer components; read as <c>vec4u</c>.
		/// </summary>
		Uint8x4 = 3,

		/// <summary>
		/// One 8-bit signed integer component; read as <c>i32</c>.
		/// </summary>
		Sint8 = 4,

		/// <summary>
		/// Two 8-bit signed integer components; read as <c>vec2i</c>.
		/// </summary>
		Sint8x2 = 5,

		/// <summary>
		/// Four 8-bit signed integer components; read as <c>vec4i</c>.
		/// </summary>
		Sint8x4 = 6,

		/// <summary>
		/// One 8-bit unsigned normalized component; read as <c>f32</c>.
		/// </summary>
		Unorm8 = 7,

		/// <summary>
		/// Two 8-bit unsigned normalized components; read as <c>vec2f</c>.
		/// </summary>
		Unorm8x2 = 8,

		/// <summary>
		/// Four 8-bit unsigned normalized components; read as <c>vec4f</c>.
		/// </summary>
		Unorm8x4 = 9,

		/// <summary>
		/// One 8-bit signed normalized component; read as <c>f32</c>.
		/// </summary>
		Snorm8 = 10,

		/// <summary>
		/// Two 8-bit signed normalized components; read as <c>vec2f</c>.
		/// </summary>
		Snorm8x2 = 11,

		/// <summary>
		/// Four 8-bit signed normalized components; read as <c>vec4f</c>.
		/// </summary>
		Snorm8x4 = 12,

		/// <summary>
		/// One 16-bit unsigned integer component; read as <c>u32</c>.
		/// </summary>
		Uint16 = 13,

		/// <summary>
		/// Two 16-bit unsigned integer components; read as <c>vec2u</c>.
		/// </summary>
		Uint16x2 = 14,

		/// <summary>
		/// Four 16-bit unsigned integer components; read as <c>vec4u</c>.
		/// </summary>
		Uint16x4 = 15,

		/// <summary>
		/// One 16-bit signed integer component; read as <c>i32</c>.
		/// </summary>
		Sint16 = 16,

		/// <summary>
		/// Two 16-bit signed integer components; read as <c>vec2i</c>.
		/// </summary>
		Sint16x2 = 17,

		/// <summary>
		/// Four 16-bit signed integer components; read as <c>vec4i</c>.
		/// </summary>
		Sint16x4 = 18,

		/// <summary>
		/// One 16-bit unsigned normalized component; read as <c>f32</c>.
		/// </summary>
		Unorm16 = 19,

		/// <summary>
		/// Two 16-bit unsigned normalized components; read as <c>vec2f</c>.
		/// </summary>
		Unorm16x2 = 20,

		/// <summary>
		/// Four 16-bit unsigned normalized components; read as <c>vec4f</c>.
		/// </summary>
		Unorm16x4 = 21,

		/// <summary>
		/// One 16-bit signed normalized component; read as <c>f32</c>.
		/// </summary>
		Snorm16 = 22,

		/// <summary>
		/// Two 16-bit signed normalized components; read as <c>vec2f</c>.
		/// </summary>
		Snorm16x2 = 23,

		/// <summary>
		/// Four 16-bit signed normalized components; read as <c>vec4f</c>.
		/// </summary>
		Snorm16x4 = 24,

		/// <summary>
		/// One 16-bit float component; read as <c>f32</c>.
		/// </summary>
		Float16 = 25,

		/// <summary>
		/// Two 16-bit float components; read as <c>vec2f</c>.
		/// </summary>
		Float16x2 = 26,

		/// <summary>
		/// Four 16-bit float components; read as <c>vec4f</c>.
		/// </summary>
		Float16x4 = 27,

		/// <summary>
		/// One 32-bit float component; read as <c>f32</c>.
		/// </summary>
		Float32 = 28,

		/// <summary>
		/// Two 32-bit float components; read as <c>vec2f</c>.
		/// </summary>
		Float32x2 = 29,

		/// <summary>
		/// Three 32-bit float components; read as <c>vec3f</c>.
		/// </summary>
		Float32x3 = 30,

		/// <summary>
		/// Four 32-bit float components; read as <c>vec4f</c>.
		/// </summary>
		Float32x4 = 31,

		/// <summary>
		/// One 32-bit unsigned integer component; read as <c>u32</c>.
		/// </summary>
		Uint32 = 32,

		/// <summary>
		/// Two 32-bit unsigned integer components; read as <c>vec2u</c>.
		/// </summary>
		Uint32x2 = 33,

		/// <summary>
		/// Three 32-bit unsigned integer components; read as <c>vec3u</c>.
		/// </summary>
		Uint32x3 = 34,

		/// <summary>
		/// Four 32-bit unsigned integer components; read as <c>vec4u</c>.
		/// </summary>
		Uint32x4 = 35,

		/// <summary>
		/// One 32-bit signed integer component; read as <c>i32</c>.
		/// </summary>
		Sint32 = 36,

		/// <summary>
		/// Two 32-bit signed integer components; read as <c>vec2i</c>.
		/// </summary>
		Sint32x2 = 37,

		/// <summary>
		/// Three 32-bit signed integer components; read as <c>vec3i</c>.
		/// </summary>
		Sint32x3 = 38,

		/// <summary>
		/// Four 32-bit signed integer components; read as <c>vec4i</c>.
		/// </summary>
		Sint32x4 = 39,

		/// <summary>
		/// 10-bit red, green, and blue and 2-bit alpha, unsigned normalized; read as <c>vec4f</c>.
		/// </summary>
		Unorm1010102 = 40,

		/// <summary>
		/// Four 8-bit unsigned normalized components in BGRA order, read as RGBA <c>vec4f</c>.
		/// </summary>
		Unorm8x4Bgra = 41,
	}
}
