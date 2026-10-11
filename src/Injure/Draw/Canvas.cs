// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Numerics;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Draws 2D shapes and textures. Obtained from an <see cref="OwnedCanvas"/>, and intended to then
/// be propagated down into code that needs to draw things. Not thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// Has a stack of <see cref="CanvasParams"/> ("the params stack").
/// <see cref="PushParams(in CanvasParamsOverride)"/> pushes an override that gets popped once the
/// returned <see cref="CanvasParamsScope"/> is disposed, creating a "scoped parameters" model.
/// The base of the stack cannot be popped off.
/// </para>
/// <para>
/// Positions and sizes are in pixels of the current target, with the origin at the top left and +Y
/// pointing down, before <see cref="CanvasParams.Transform"/> is applied.
/// </para>
/// <para>
/// Draws may be batched; the exact batching behavior is unspecified. Draws are never reordered.
/// </para>
/// <para>
/// Only triangles are ever drawn. Geometry for lines, line lists, line strips, triangle strips, and
/// polygons is generated as triangles, rather than using the corresponding GPU primitives. There's
/// no face culling, so winding order doesn't affect visual output.
/// </para>
/// <para>
/// Colors are sRGB-encoded, and blending happens on the encoded values. Targets must therefore have
/// non-sRGB formats, so the values reach them unchanged; see
/// <see cref="SurfaceFormatPolicy.PreferNonSrgb"/>. Texture texels are likewise taken as stored, so
/// textures should hold sRGB-encoded colors in non-sRGB formats.
/// </para>
/// <para>
/// The name clashes with the <c>Canvas</c> controls of Avalonia and WPF, and is similar to MAUI's
/// <c>ICanvas</c>. Code that runs into such a conflict can alias this type, e.g. <c>global using
/// IjCanvas = Injure.Draw.Canvas;</c>.
/// </para>
/// <para>
/// Infomative: untextured draws actually use a 1x1 <see cref="TextureInterpretation.Color"/> white
/// texture, and <see cref="CanvasParams.Transform"/> is applied on the CPU rather than in a vertex
/// shader. Both of these allow for particular internal optimizations. The functional behavior is
/// unchanged, and the optimizations heavily outweigh the miniscule added overhead.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly ref struct Canvas {
	private const int maxStackIndices = 512; // 2048 bytes

	private readonly OwnedCanvas? ownedCanvas;

	internal Canvas(OwnedCanvas ownedCanvas) {
		this.ownedCanvas = ownedCanvas;
	}

	private OwnedCanvas o => ownedCanvas ?? throw new InvalidOperationException("this Canvas value is invalid/uninitialized");

	private static ReadOnlySpan<uint> triangleIndices => [0, 1, 2];
	// for corners in topLeft, topRight, bottomLeft, bottomRight order; ccw
	private static ReadOnlySpan<uint> quadIndices => [0, 2, 1, 3, 1, 2];

	/// <summary>
	/// The currently active canvas parameters, i.e. the top of the params stack.
	/// </summary>
	public CanvasParams CurrentParams => o.CurrentParams;

	/// <summary>
	/// The drawable width in pixels of the active target.
	/// </summary>
	public uint TargetWidth => o.TargetWidth;

	/// <summary>
	/// The drawable height in pixels of the active target.
	/// </summary>
	public uint TargetHeight => o.TargetHeight;

	// ==========================================================================
	// primitives

	/// <summary>
	/// Draws a filled triangle with interpolated per-vertex colors.
	/// </summary>
	/// <param name="a">First vertex of the triangle.</param>
	/// <param name="b">Second vertex of the triangle.</param>
	/// <param name="c">Third vertex of the triangle.</param>
	public void Triangle(Vertex2dColor a, Vertex2dColor b, Vertex2dColor c) =>
		o.AddPrimitive([a, b, c], triangleIndices);

	/// <summary>
	/// Draws a filled flat-color triangle.
	/// </summary>
	/// <param name="a">First vertex of the triangle.</param>
	/// <param name="b">Second vertex of the triangle.</param>
	/// <param name="c">Third vertex of the triangle.</param>
	/// <param name="color">Fill color.</param>
	public void Triangle(Vector2 a, Vector2 b, Vector2 c, SrgbColor32 color) =>
		o.AddPrimitive([a, b, c], color, triangleIndices);

	/// <summary>
	/// Draws a filled convex quadrilateral with interpolated per-corner colors.
	/// </summary>
	/// <param name="topLeft">Intended top-left corner of the quad.</param>
	/// <param name="topRight">Intended top-right corner of the quad.</param>
	/// <param name="bottomLeft">Intended bottom-left corner of the quad.</param>
	/// <param name="bottomRight">Intended bottom-right corner of the quad.</param>
	/// <remarks>
	/// Non-convex (concave or self-intersecting) quads produce incorrect visual output rather than
	/// being rejected. The quad is split into two triangles along the diagonal from
	/// <paramref name="topRight"/> to <paramref name="bottomLeft"/>.
	/// </remarks>
	public void ConvexQuad(Vertex2dColor topLeft, Vertex2dColor topRight, Vertex2dColor bottomLeft, Vertex2dColor bottomRight) =>
		o.AddPrimitive([topLeft, topRight, bottomLeft, bottomRight], quadIndices);

	/// <summary>
	/// Draws a filled flat-color convex quadrilateral.
	/// </summary>
	/// <param name="topLeft">Intended top-left corner of the quad.</param>
	/// <param name="topRight">Intended top-right corner of the quad.</param>
	/// <param name="bottomLeft">Intended bottom-left corner of the quad.</param>
	/// <param name="bottomRight">Intended bottom-right corner of the quad.</param>
	/// <param name="color">Fill color.</param>
	/// <remarks>
	/// Non-convex (concave or self-intersecting) quads produce incorrect visual output rather than
	/// being rejected. The quad is split into two triangles along the diagonal from
	/// <paramref name="topRight"/> to <paramref name="bottomLeft"/>.
	/// </remarks>
	public void ConvexQuad(Vector2 topLeft, Vector2 topRight, Vector2 bottomLeft, Vector2 bottomRight, SrgbColor32 color) =>
		o.AddPrimitive([topLeft, topRight, bottomLeft, bottomRight], color, quadIndices);

	/// <summary>
	/// Draws a filled rectangle with interpolated per-corner colors.
	/// </summary>
	/// <param name="rect">The rectangle.</param>
	/// <param name="colorTopLeft">Color for the top-left corner.</param>
	/// <param name="colorTopRight">Color for the top-right corner.</param>
	/// <param name="colorBottomLeft">Color for the bottom-left corner.</param>
	/// <param name="colorBottomRight">Color for the bottom-right corner.</param>
	public void Rect(RectF rect, SrgbColor32 colorTopLeft, SrgbColor32 colorTopRight, SrgbColor32 colorBottomLeft, SrgbColor32 colorBottomRight) =>
		o.AddPrimitive(
			[
				new Vertex2dColor(rect.X, rect.Y, colorTopLeft),
				new Vertex2dColor(rect.X + rect.Width, rect.Y, colorTopRight),
				new Vertex2dColor(rect.X, rect.Y + rect.Height, colorBottomLeft),
				new Vertex2dColor(rect.X + rect.Width, rect.Y + rect.Height, colorBottomRight),
			],
			quadIndices
		);

	/// <summary>
	/// Draws a filled flat-color rectangle.
	/// </summary>
	/// <param name="rect">The rectangle.</param>
	/// <param name="color">Fill color.</param>
	public void Rect(RectF rect, SrgbColor32 color) =>
		Rect(rect, color, color, color, color);

	/// <summary>
	/// Draws a line segment with interpolated per-endpoint colors.
	/// </summary>
	/// <param name="a">Line start point.</param>
	/// <param name="b">Line end point.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Generates a thin quad rather than using a line primitive.
	/// </para>
	/// <para>
	/// If the endpoints coincide, or almost coincide (currently, distance &lt;= 1e-4), nothing is
	/// drawn.
	/// </para>
	/// </remarks>
	public void Line(Vertex2dColor a, Vertex2dColor b, float thickness = 1f) {
		chkThickness(thickness);
		OwnedCanvas oc = o;

		const float epsilon = 1e-4f;
		Vector2 direction = (Vector2)b - (Vector2)a;
		float len = direction.Length();
		if (len < epsilon)
			return;
		direction /= len;
		Vector2 normal = new Vector2(-direction.Y, direction.X) * thickness * 0.5f;
		Vector2 p0 = (Vector2)a + normal;
		Vector2 p1 = (Vector2)b + normal;
		Vector2 p2 = (Vector2)b - normal;
		Vector2 p3 = (Vector2)a - normal;
		ReadOnlySpan<Vertex2dColor> verts = [
			new Vertex2dColor(p0, a.Color),
			new Vertex2dColor(p1, b.Color),
			new Vertex2dColor(p2, b.Color),
			new Vertex2dColor(p3, a.Color),
		];
		// keep the generated triangles ccw
		if (winding(p0, p1, p2) > 0f)
			oc.AddPrimitive(verts, [0, 2, 1, 0, 3, 2]);
		else
			oc.AddPrimitive(verts, [0, 1, 2, 0, 2, 3]);
	}

	/// <summary>
	/// Draws a flat-color line segment.
	/// </summary>
	/// <param name="a">Line start point.</param>
	/// <param name="b">Line end point.</param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Generates a thin quad rather than using a line primitive.
	/// </para>
	/// <para>
	/// If the endpoints coincide, or almost coincide (currently, distance &lt;= 1e-4), nothing is
	/// drawn.
	/// </para>
	/// </remarks>
	public void Line(Vector2 a, Vector2 b, SrgbColor32 color, float thickness = 1f) =>
		Line(new Vertex2dColor(a, color), new Vertex2dColor(b, color), thickness);

	/// <summary>
	/// Draws a filled convex polygon with interpolated per-vertex colors.
	/// </summary>
	/// <param name="verts">Vertices of the polygon in order around its edge.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="verts"/> has fewer than 3 vertices.
	/// </exception>
	/// <remarks>
	/// Non-convex polygons produce incorrect visual output rather than being rejected. The polygon is
	/// triangulated as a fan from the first vertex.
	/// </remarks>
	public void ConvexPoly(ReadOnlySpan<Vertex2dColor> verts) {
		OwnedCanvas oc = o;
		if (verts.Length < 3)
			throw new ArgumentException("a polygon needs at least 3 vertices", nameof(verts));
		withIndices((verts.Length - 2) * 3, fanIndices, verts, static (oc, v, idx, _) => oc.AddPrimitive(v, idx), 0, oc);
	}

	/// <summary>
	/// Draws a filled flat-color convex polygon.
	/// </summary>
	/// <param name="points">Vertices of the polygon in order around its edge.</param>
	/// <param name="color">Fill color.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="points"/> has fewer than 3 points.
	/// </exception>
	/// <remarks>
	/// Non-convex polygons produce incorrect visual output rather than being rejected. The polygon is
	/// triangulated as a fan from the first point.
	/// </remarks>
	public void ConvexPoly(ReadOnlySpan<Vector2> points, SrgbColor32 color) {
		OwnedCanvas oc = o;
		if (points.Length < 3)
			throw new ArgumentException("a polygon needs at least 3 points", nameof(points));
		withIndices((points.Length - 2) * 3, fanIndices, points, static (oc, p, idx, c) => oc.AddPrimitive(p, c, idx), color, oc);
	}

	/// <summary>
	/// Draws a filled triangle list with interpolated per-vertex colors.
	/// </summary>
	/// <param name="verts">Vertices forming the triangles.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="verts"/> is empty or its length is not a multiple of 3.
	/// </exception>
	public void TriangleList(ReadOnlySpan<Vertex2dColor> verts) {
		OwnedCanvas oc = o;
		if (verts.Length == 0 || verts.Length % 3 != 0)
			throw new ArgumentException("triangle list vertex count must be a positive multiple of 3", nameof(verts));
		withIndices(verts.Length, listIndices, verts, static (oc, v, idx, _) => oc.AddPrimitive(v, idx), 0, oc);
	}

	/// <summary>
	/// Draws a filled flat-color triangle list.
	/// </summary>
	/// <param name="points">Vertices forming the triangles.</param>
	/// <param name="color">Fill color.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="points"/> is empty or its length is not a multiple of 3.
	/// </exception>
	public void TriangleList(ReadOnlySpan<Vector2> points, SrgbColor32 color) {
		OwnedCanvas oc = o;
		if (points.Length == 0 || points.Length % 3 != 0)
			throw new ArgumentException("triangle list vertex count must be a positive multiple of 3", nameof(points));
		withIndices(points.Length, listIndices, points, static (oc, p, idx, c) => oc.AddPrimitive(p, c, idx), color, oc);
	}

	/// <summary>
	/// Draws a filled triangle strip with interpolated per-vertex colors.
	/// </summary>
	/// <param name="verts">Vertices forming the triangle strip.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="verts"/> has fewer than 3 vertices.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Generates the geometry rather than using a triangle strip primitive.
	/// </para>
	/// <para>
	/// Triangle <c>k</c> (counting from 0) is vertices <c>k</c>, <c>k + 1</c>, <c>k + 2</c> in that
	/// order for even <c>k</c>, and <c>k + 1</c>, <c>k</c>, <c>k + 2</c> for odd <c>k</c>, so that all
	/// the triangles have the same winding as the first one. Degenerate triangles are allowed and, as
	/// one would expect, cover no pixels.
	/// </para>
	/// </remarks>
	public void TriangleStrip(ReadOnlySpan<Vertex2dColor> verts) {
		OwnedCanvas oc = o;
		if (verts.Length < 3)
			throw new ArgumentException("a triangle strip needs at least 3 vertices", nameof(verts));
		withIndices((verts.Length - 2) * 3, stripIndices, verts, static (oc, v, idx, _) => oc.AddPrimitive(v, idx), 0, oc);
	}

	/// <summary>
	/// Draws a filled flat-color triangle strip.
	/// </summary>
	/// <param name="points">Vertices forming the triangle strip.</param>
	/// <param name="color">Fill color.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="points"/> has fewer than 3 points.
	/// </exception>
	/// <inheritdoc cref="TriangleStrip(ReadOnlySpan{Vertex2dColor})" path="/remarks"/>
	public void TriangleStrip(ReadOnlySpan<Vector2> points, SrgbColor32 color) {
		OwnedCanvas oc = o;
		if (points.Length < 3)
			throw new ArgumentException("a triangle strip needs at least 3 points", nameof(points));
		withIndices((points.Length - 2) * 3, stripIndices, points, static (oc, p, idx, c) => oc.AddPrimitive(p, c, idx), color, oc);
	}

	/// <summary>
	/// Draws a line list with interpolated per-endpoint colors.
	/// </summary>
	/// <param name="verts">Points forming the lines, two per line.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="verts"/> is empty or its length is not a multiple of 2.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// Generates the geometry rather than using a line list primitive; each line is drawn as by
	/// <see cref="Line(Vertex2dColor, Vertex2dColor, float)"/>.
	/// </remarks>
	public void LineList(ReadOnlySpan<Vertex2dColor> verts, float thickness = 1f) {
		chkThickness(thickness);
		if (verts.Length == 0 || verts.Length % 2 != 0)
			throw new ArgumentException("line list vertex count must be a positive multiple of 2", nameof(verts));
		for (int i = 0; i < verts.Length; i += 2)
			Line(verts[i], verts[i + 1], thickness);
	}

	/// <summary>
	/// Draws a flat-color line list.
	/// </summary>
	/// <param name="points">Points forming the lines, two per line.</param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="points"/> is empty or its length is not a multiple of 2.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// Generates the geometry rather than using a line list primitive; each line is drawn as by
	/// <see cref="Line(Vector2, Vector2, SrgbColor32, float)"/>.
	/// </remarks>
	public void LineList(ReadOnlySpan<Vector2> points, SrgbColor32 color, float thickness = 1f) {
		chkThickness(thickness);
		if (points.Length == 0 || points.Length % 2 != 0)
			throw new ArgumentException("line list vertex count must be a positive multiple of 2", nameof(points));
		for (int i = 0; i < points.Length; i += 2)
			Line(points[i], points[i + 1], color, thickness);
	}

	/// <summary>
	/// Draws a line strip with interpolated per-endpoint colors.
	/// </summary>
	/// <param name="verts">Points forming the line strip.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="verts"/> has fewer than 2 vertices.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// Generates the geometry rather than using a line strip primitive. Each segment is drawn as a
	/// separate line, as by <see cref="Line(Vertex2dColor, Vertex2dColor, float)"/>, so joints have no
	/// join geometry, and the segments overlap there.
	/// </remarks>
	public void LineStrip(ReadOnlySpan<Vertex2dColor> verts, float thickness = 1f) {
		chkThickness(thickness);
		if (verts.Length < 2)
			throw new ArgumentException("a line strip needs at least 2 vertices", nameof(verts));
		for (int i = 0; i + 1 < verts.Length; i++)
			Line(verts[i], verts[i + 1], thickness);
	}

	/// <summary>
	/// Draws a flat-color line strip.
	/// </summary>
	/// <param name="points">Points forming the line strip.</param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="points"/> has fewer than 2 points.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// Generates the geometry rather than using a line strip primitive. Each segment is drawn as a
	/// separate line, as by <see cref="Line(Vector2, Vector2, SrgbColor32, float)"/>, so joints have
	/// no join geometry, and the segments overlap there.
	/// </remarks>
	public void LineStrip(ReadOnlySpan<Vector2> points, SrgbColor32 color, float thickness = 1f) {
		chkThickness(thickness);
		if (points.Length < 2)
			throw new ArgumentException("a line strip needs at least 2 points", nameof(points));
		for (int i = 0; i + 1 < points.Length; i++)
			Line(points[i], points[i + 1], color, thickness);
	}

	// ==========================================================================
	// textures

	/// <summary>
	/// Draws a texture, or a region of it, optionally transformed and tinted.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="draw">
	/// Where to draw it, which part of it, how it's transformed, and with what tint. A
	/// <see cref="Vector2"/> (top-left position, natural size) or <see cref="RectF"/> (destination
	/// rect) converts to this implicitly.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="tex"/> or <paramref name="draw"/>'s <see cref="TexDraw.Destination"/>
	/// is the <see langword="default"/> value, or if <paramref name="draw"/>'s
	/// <see cref="TexDraw.Origin"/>, <see cref="TexDraw.Scale"/>, or <see cref="TexDraw.Rotation"/>
	/// isn't finite.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void Texture(TextureSource tex, in TexDraw draw) {
		OwnedCanvas oc = o;
		if (tex.IsDefault)
			throw new ArgumentException("texture source is the default value", nameof(tex));
		if (draw.Destination.IsDefault)
			throw new ArgumentException("texture draw destination is the default value", nameof(draw));
		if (!float.IsFinite(draw.Origin.X) || !float.IsFinite(draw.Origin.Y) || !float.IsFinite(draw.Scale.X) || !float.IsFinite(draw.Scale.Y) || !float.IsFinite(draw.Rotation))
			throw new ArgumentException("texture draw origin, scale, and rotation must be finite", nameof(draw));
		ResolvedTextureSource r = tex.Resolve();
		(RectF uv, float w, float h) = draw.Source.Resolve(r.Width, r.Height);
		(Vector2 pivot, Vector2 size) = draw.Destination.Resolve(w, h, draw.Origin);

		Vector2 tl = -draw.Origin * size * draw.Scale;
		Vector2 br = (Vector2.One - draw.Origin) * size * draw.Scale;
		Vector2 rotTl = tl, rotTr = new(br.X, tl.Y), rotBl = new(tl.X, br.Y), rotBr = br;
		if (draw.Rotation != 0f) {
			(float sin, float cos) = MathF.SinCos(draw.Rotation);
			rotTl = rot(rotTl, sin, cos);
			rotTr = rot(rotTr, sin, cos);
			rotBl = rot(rotBl, sin, cos);
			rotBr = rot(rotBr, sin, cos);
		}
		rotTl += pivot;
		rotTr += pivot;
		rotBl += pivot;
		rotBr += pivot;

		float u0 = uv.X, u1 = uv.X + uv.Width, v0 = uv.Y, v1 = uv.Y + uv.Height;
		if (draw.FlipX)
			(u0, u1) = (u1, u0);
		if (draw.FlipY)
			(v0, v1) = (v1, v0);
		SrgbColor32 c = draw.Tint;
		oc.AddTextured(
			r,
			[
				new Vertex2dTextureColor(rotTl.X, rotTl.Y, u0, v0, c),
				new Vertex2dTextureColor(rotTr.X, rotTr.Y, u1, v0, c),
				new Vertex2dTextureColor(rotBl.X, rotBl.Y, u0, v1, c),
				new Vertex2dTextureColor(rotBr.X, rotBr.Y, u1, v1, c),
			],
			quadIndices
		);
	}

	/// <summary>
	/// Positive angles turn +X towards +Y, i.e. clockwise since +Y = down.
	/// </summary>
	private static Vector2 rot(Vector2 v, float sin, float cos) =>
		new(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);

	// ==========================================================================
	// params

	/// <summary>
	/// Pushes a temporary parameter override onto the parameter stack.
	/// </summary>
	/// <param name="ov">Partial override applied on top of <see cref="CurrentParams"/>.</param>
	/// <returns>
	/// A scope that restores the previous parameters when disposed. Disposing it while a later
	/// override is still pushed throws <see cref="InvalidOperationException"/>.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// Thrown if the resulting parameters are invalid: they target a texture with an sRGB format, have
	/// a scissor with a negative size, or use <see cref="TextureInterpretation.Sdf"/> without
	/// <see cref="CanvasMaterial.SdfParams"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the resulting parameters target the primary output and the frame has none.
	/// </exception>
	/// <remarks>
	/// This is the standard way to temporarily change the transform, target, etc. Changing the target
	/// or color attachment ops ends the current render pass; see <see cref="CanvasParams"/>.
	/// </remarks>
	public CanvasParamsScope PushParams(scoped in CanvasParamsOverride ov) => o.PushParams(in ov);

	/// <summary>
	/// Pushes a temporary parameter override onto the parameter stack.
	/// </summary>
	/// <inheritdoc cref="PushParams(in CanvasParamsOverride)" path="/returns"/>
	/// <remarks>
	/// <para>
	/// This is the standard way to temporarily change the transform, target, etc. Changing the target
	/// or color attachment ops ends the current render pass; see <see cref="CanvasParams"/>.
	/// </para>
	/// <para>
	/// Convenience overload that builds a <see cref="CanvasParamsOverride"/> from its parameters,
	/// which mirror that type's fields.
	/// </para>
	/// </remarks>
	/// <inheritdoc cref="PushParams(in CanvasParamsOverride)" path="/exception"/>
	public CanvasParamsScope PushParams(
		CanvasTarget? target = null,
		ColorAttachmentOps? colorAttachmentOps = null,
		CanvasScissor? scissor = null,
		Matrix3x2? transform = null,
		CanvasOutputState? outputState = null,
		CanvasMaterial? material = null
	) => o.PushParams(new CanvasParamsOverride(target, colorAttachmentOps, scissor, transform, outputState, material));

	/// <summary>
	/// Pushes a temporary parameter override onto the parameter stack, or does nothing and returns a
	/// no-op scope if <paramref name="ov"/> is <see langword="null"/>.
	/// </summary>
	/// <param name="ov">
	/// Partial override applied on top of <see cref="CurrentParams"/>, or <see langword="null"/>.
	/// </param>
	/// <returns>
	/// A scope that restores the previous parameters when disposed, or the <see langword="default"/>
	/// <see cref="CanvasParamsScope"/>, which does nothing, if <paramref name="ov"/> is
	/// <see langword="null"/>.
	/// </returns>
	/// <remarks>
	/// Lets APIs return <c>CanvasParamsOverride?</c>, e.g. <c>using
	/// (cv.PushParamsIfNonnull(foo.GetCanvasParams(bar, baz))) { ... }</c>.
	/// </remarks>
	/// <inheritdoc cref="PushParams(in CanvasParamsOverride)" path="/exception"/>
	public CanvasParamsScope PushParamsIfNonnull(scoped in CanvasParamsOverride? ov) {
		OwnedCanvas oc = o;
		return ov is CanvasParamsOverride nonnull ? oc.PushParams(in nonnull) : default;
	}

	// ==========================================================================
	// helpers
	private static void chkThickness(float thickness) {
		// written so that NaN fails too
		if (!(thickness > 0f) || !float.IsFinite(thickness))
			throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "line thickness must be finite and positive");
	}

	private static float winding(Vector2 a, Vector2 b, Vector2 c) =>
		// in cartesian coordinates (+Y = up), >0 = ccw and <0 = cw
		// in screen space coords, +Y is down, but i'm pretty sure that's just the ortho projection and
		// +Y in clip space coords points up so >0 = ccw / <0 = cw
		(b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

	private delegate void IndexGen(Span<uint> dst, int vertexCount);
	private delegate void AddWithIndices<T, TState>(OwnedCanvas oc, ReadOnlySpan<T> verts, ReadOnlySpan<uint> indices, TState state);

	private static void fanIndices(Span<uint> dst, int n) {
		for (int i = 1, j = 0; i < n - 1; i++) {
			dst[j++] = 0;
			dst[j++] = (uint)i;
			dst[j++] = (uint)i + 1;
		}
	}

	private static void listIndices(Span<uint> dst, int n) {
		for (int i = 0; i < n; i++)
			dst[i] = (uint)i;
	}

	private static void stripIndices(Span<uint> dst, int n) {
		for (int i = 0, j = 0; i < n - 2; i++) {
			if ((i & 1) == 0) {
				dst[j++] = (uint)i;
				dst[j++] = (uint)i + 1;
			} else {
				dst[j++] = (uint)i + 1;
				dst[j++] = (uint)i;
			}
			dst[j++] = (uint)i + 2;
		}
	}

	private static void withIndices<T, TState>(int indexCount, IndexGen gen, ReadOnlySpan<T> verts, AddWithIndices<T, TState> add, TState state, OwnedCanvas oc)
		where T : struct where TState : struct {
		uint[]? rented = null;
		Span<uint> idx = indexCount <= maxStackIndices
			? stackalloc uint[indexCount]
			: (rented = ArrayPool<uint>.Shared.Rent(indexCount)).AsSpan(0, indexCount);
		try {
			gen(idx, verts.Length);
			add(oc, verts, idx, state);
		} finally {
			if (rented is not null)
				ArrayPool<uint>.Shared.Return(rented);
		}
	}
}
