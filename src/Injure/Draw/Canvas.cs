// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Collections.Immutable;
using System.Numerics;
using Injure.Assets;
using Injure.Draw.Text;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Draws 2D shapes, textures, and optionally text. Obtained from an <see cref="OwnedCanvas"/>, and
/// intended to then be propagated down into code that needs to draw things. Not thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// Has a notion of a <i>drawable area</i>: the drawable area is measured in <i>drawable units</i>,
/// abbreviated "du" when used as a unit, with the origin at its top left and +Y pointing down. The
/// drawable area is defined by the current target's <see cref="CanvasTarget.Viewport"/>. One
/// drawable unit is a square; its side length in target pixels is queryable via
/// <see cref="PixelsPerUnit"/>.
/// </para>
/// <para>
/// Has a stack of <see cref="CanvasParams"/> ("the params stack").
/// <see cref="PushParams(in CanvasParamsOverride)"/> pushes an override that gets popped once the
/// returned <see cref="CanvasParamsScope"/> is disposed, creating a "scoped parameters" model. The
/// base of the stack cannot be popped off.
/// </para>
/// <para>
/// Positions and sizes are in <i>draw coordinates</i>, which <see cref="CanvasParams.Transform"/>
/// maps to the drawable area. With the identity transform, draw coordinates concide with positions
/// in the drawable area. Every draw is clipped to the drawable area, and to
/// <see cref="CanvasParams.Scissor"/> within it.
/// </para>
/// <para>
/// Draws may be batched; the exact batching behavior is unspecified. Draws are never reordered.
/// </para>
/// <para>
/// Only triangles are ever drawn; geometry for all other shapes is generated as triangles rather
/// than using line/list/strip/etc GPU primitives. Curved shapes are approximated by polygons.
/// There is no face culling, so winding order doesn't affect visual output.
/// </para>
/// <para>
/// Colors are sRGB-encoded, and blending happens on the encoded values. Targets must therefore have
/// non-sRGB formats, so the values reach them unchanged; see
/// <see cref="SurfaceFormatPolicy.PreferNonSrgb"/>. Texture texels are likewise taken as stored, so
/// textures should hold sRGB-encoded colors in non-sRGB formats.
/// </para>
/// <para>
/// Has built-in <see cref="TextSystem"/> integration for convenience:
/// <see cref="Text(ReadOnlySpan{char}, in TextStyle, Vector2, TextLayoutOptions?, SrgbColor32?)"/>
/// and <see cref="Text(RichText, Vector2, TextLayoutOptions?, SrgbColor32?)"/> lay out the text on
/// the spot through a <see cref="TextSystem"/> passed to the <see cref="OwnedCanvas"/> at creation,
/// and cache the layout for later draws.
/// </para>
/// <para>
/// The name clashes with the <c>Canvas</c> controls of Avalonia and WPF, and is similar to MAUI's
/// <c>ICanvas</c>. Code that runs into such a conflict can alias this type, e.g.
/// <c>global using IjCanvas = Injure.Draw.Canvas;</c>.
/// </para>
/// <para>
/// Informative: untextured draws actually use a 1x1 <see cref="TextureInterpretation.Color"/> white
/// texture, and <see cref="CanvasParams.Transform"/> and <see cref="CanvasTarget.Viewport"/> are
/// applied on the CPU rather than in a vertex shader. Both of these allow for particular internal
/// optimizations. The functional behavior is unchanged, and the optimizations heavily outweigh the
/// miniscule added overhead.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly ref struct Canvas : IHasInvalidDefault {
	private const int maxStackIndices = 512; // 2048 bytes

	private readonly OwnedCanvas? ownedCanvas;

	internal Canvas(OwnedCanvas ownedCanvas) {
		this.ownedCanvas = ownedCanvas;
	}

	private OwnedCanvas o => ownedCanvas ?? throw InvalidDefaultException.For<Canvas>();

	private static readonly ImmutableArray<uint> triangleIndices = [0, 1, 2];
	// for corners in topLeft, topRight, bottomLeft, bottomRight order; ccw
	private static readonly ImmutableArray<uint> quadIndices = [0, 2, 1, 3, 1, 2];

	/// <summary>
	/// The top of the params stack, i.e. the currently active canvas parameters.
	/// </summary>
	public CanvasParams CurrentParams => o.CurrentParams;

	/// <summary>
	/// The current transform, i.e. <see cref="CanvasParams.Transform"/> of
	/// <see cref="CurrentParams"/>, mapping draw coordinates to the drawable area. Always invertible
	/// (see <see cref="MatrixInvert"/>).
	/// </summary>
	/// <remarks>
	/// Doesn't include the mapping from drawable units to target pixels; see
	/// <see cref="EffectiveTransform"/>.
	/// </remarks>
	public Matrix3x2 CurrentTransform => o.CurrentParams.Transform;

	/// <summary>
	/// The active target's drawable area, i.e. <see cref="CanvasTarget.Viewport"/> of
	/// <see cref="CanvasParams.Target"/> of <see cref="CurrentParams"/>.
	/// </summary>
	public Viewport CurrentViewport => o.CurrentParams.Target.Viewport;

	/// <summary>
	/// The matrix that maps draw coordinates to pixels of the active target, i.e.
	/// <c>CurrentTransform * CurrentViewport.ToPixelsMatrix</c>, or
	/// <see cref="CanvasParams.EffectiveTransform"/> of <see cref="CurrentParams"/>. Always invertible
	/// (see <see cref="MatrixInvert"/>).
	/// </summary>
	public Matrix3x2 EffectiveTransform => o.EffectiveTransform;

	/// <summary>
	/// The width of the drawable area, in drawable units.
	/// </summary>
	/// <remarks>
	/// Unaffected by <see cref="CurrentTransform"/>. Can be fractional if the
	/// <see cref="CurrentViewport"/> is configured that way, for example
	/// <see cref="Game.PrimaryViewMode.LogicalUnits"/> on a fractional pixel density setup.
	/// </remarks>
	public float Width => CurrentViewport.Size.X;

	/// <summary>
	/// The height of the drawable area, in drawable units.
	/// </summary>
	/// <inheritdoc cref="Width" path="/remarks"/>
	public float Height => CurrentViewport.Size.Y;

	/// <summary>
	/// The side length of one drawable unit, in target pixels, i.e.
	/// <see cref="Viewport.PixelsPerUnit"/> of <see cref="CurrentViewport"/>.
	/// </summary>
	public float PixelsPerUnit => CurrentViewport.PixelsPerUnit;

	/// <summary>
	/// The width of the whole active target, in pixels.
	/// </summary>
	/// <remarks>
	/// <b>This is probably not what you want.</b> This is in target pixels, not drawable units, and
	/// includes everything outside of the drawable area, such as letterbox bars. See
	/// <see cref="Width"/>/<see cref="Height"/> for the size of the drawable area.
	/// </remarks>
	public uint TargetPixelWidth => o.TargetPixelWidth;

	/// <summary>
	/// The height of the whole active target, in pixels.
	/// </summary>
	/// <inheritdoc cref="TargetPixelWidth" path="/remarks"/>
	public uint TargetPixelHeight => o.TargetPixelHeight;

	/// <summary>
	/// Replaces the active target's contents with <paramref name="color"/>, subject to the current
	/// scissor.
	/// </summary>
	/// <param name="color">Color to fill with.</param>
	/// <remarks>
	/// This only affects the drawable area, or the scissor rectangle within it. The current transform, output
	/// state, and write mask don't apply; the color replaces the target's contents on all channels,
	/// including alpha. Unlike clearing through <see cref="ColorAttachmentOps"/>, this doesn't end the
	/// current render pass.
	/// </remarks>
	public void Clear(SrgbColor32 color) => o.Clear(color);

	// ==========================================================================
	// primitives

	/// <summary>
	/// Draws a filled triangle with interpolated per-vertex colors.
	/// </summary>
	/// <param name="a">First vertex of the triangle.</param>
	/// <param name="b">Second vertex of the triangle.</param>
	/// <param name="c">Third vertex of the triangle.</param>
	public void Triangle(Vertex2dColor a, Vertex2dColor b, Vertex2dColor c) =>
		o.AddPrimitive([a, b, c], triangleIndices.AsSpan());

	/// <summary>
	/// Draws a filled flat-color triangle.
	/// </summary>
	/// <param name="a">First vertex of the triangle.</param>
	/// <param name="b">Second vertex of the triangle.</param>
	/// <param name="c">Third vertex of the triangle.</param>
	/// <param name="color">Fill color.</param>
	public void Triangle(Vector2 a, Vector2 b, Vector2 c, SrgbColor32 color) =>
		o.AddPrimitive([a, b, c], color, triangleIndices.AsSpan());

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
		o.AddPrimitive([topLeft, topRight, bottomLeft, bottomRight], quadIndices.AsSpan());

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
		o.AddPrimitive([topLeft, topRight, bottomLeft, bottomRight], color, quadIndices.AsSpan());

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
			quadIndices.AsSpan()
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
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="cap">How the line is drawn at both ends.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Generates the geometry rather than using a line primitive. Round caps get as many edges as the
	/// matching part of <see cref="Circle(Vector2, float, SrgbColor32, int?)"/> would.
	/// </para>
	/// <para>
	/// If the endpoints coincide, or almost coincide (currently, distance &lt; 1e-4), nothing is
	/// drawn, whatever the cap.
	/// </para>
	/// </remarks>
	public void Line(Vertex2dColor a, Vertex2dColor b, float thickness = 1f, LineCap cap = default) {
		chkThickness(thickness);
		stroke(o, [a, b], [], default, thickness, closed: false, default, cap);
	}

	/// <summary>
	/// Draws a flat-color line segment.
	/// </summary>
	/// <param name="a">Line start point.</param>
	/// <param name="b">Line end point.</param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="cap">How the line is drawn at both ends.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <inheritdoc cref="Line(Vertex2dColor, Vertex2dColor, float, LineCap)" path="/remarks"/>
	public void Line(Vector2 a, Vector2 b, SrgbColor32 color, float thickness = 1f, LineCap cap = default) {
		chkThickness(thickness);
		stroke(o, [], [a, b], color, thickness, closed: false, default, cap);
	}

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
	/// one would expect, cover no drawable units.
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
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="cap">How each line is drawn at both ends.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="verts"/> is empty or its length is not a multiple of 2.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// Generates the geometry rather than using a line list primitive; each line is drawn as by
	/// <see cref="Line(Vertex2dColor, Vertex2dColor, float, LineCap)"/>. The lines are independent, so
	/// lines that touch or cross overlap there, and translucent ones blend twice. Use
	/// <see cref="LineStrip(ReadOnlySpan{Vertex2dColor}, float, LineJoin, LineCap)"/> for connected
	/// lines.
	/// </remarks>
	public void LineList(ReadOnlySpan<Vertex2dColor> verts, float thickness = 1f, LineCap cap = default) {
		chkThickness(thickness);
		if (verts.Length == 0 || verts.Length % 2 != 0)
			throw new ArgumentException("line list vertex count must be a positive multiple of 2", nameof(verts));
		OwnedCanvas oc = o;
		for (int i = 0; i < verts.Length; i += 2)
			stroke(oc, verts.Slice(i, 2), [], default, thickness, closed: false, default, cap);
	}

	/// <summary>
	/// Draws a flat-color line list.
	/// </summary>
	/// <param name="points">Points forming the lines, two per line.</param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="cap">How each line is drawn at both ends.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="points"/> is empty or its length is not a multiple of 2.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// Generates the geometry rather than using a line list primitive; each line is drawn as by
	/// <see cref="Line(Vector2, Vector2, SrgbColor32, float, LineCap)"/>. The lines are independent,
	/// so lines that touch or cross overlap there, and translucent ones blend twice. Use
	/// <see cref="LineStrip(ReadOnlySpan{Vector2}, SrgbColor32, float, LineJoin, LineCap)"/> for
	/// connected lines.
	/// </remarks>
	public void LineList(ReadOnlySpan<Vector2> points, SrgbColor32 color, float thickness = 1f, LineCap cap = default) {
		chkThickness(thickness);
		if (points.Length == 0 || points.Length % 2 != 0)
			throw new ArgumentException("line list vertex count must be a positive multiple of 2", nameof(points));
		OwnedCanvas oc = o;
		for (int i = 0; i < points.Length; i += 2)
			stroke(oc, [], points.Slice(i, 2), color, thickness, closed: false, default, cap);
	}

	/// <summary>
	/// Draws a line strip with interpolated per-vertex colors.
	/// </summary>
	/// <param name="verts">Points forming the line strip.</param>
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="join">How the strip is drawn where two segments meet.</param>
	/// <param name="cap">How the strip is drawn at its first and last points.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="verts"/> has fewer than 2 vertices.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Generates the geometry rather than using a line strip primitive. Round joins and caps get as
	/// many edges as the matching part of <see cref="Circle(Vector2, float, SrgbColor32, int?)"/>
	/// would. Joints where the strip goes on (almost) straight use a miter join regardless of
	/// <paramref name="join"/>, since the choice doesn't matter there.
	/// </para>
	/// <para>
	/// The generated triangles don't overlap, so translucent strips blend once everywhere, with two
	/// exceptions: a strip that crosses itself, or whose caps or joins reach over another part of it,
	/// overlaps there, and so can a segment shorter than about the thickness next to a sharp joint.
	/// </para>
	/// <para>
	/// Consecutive points that coincide or almost coincide (currently, distance &lt; 1e-4) count as
	/// one. If that leaves a single point, nothing is drawn, whatever the cap.
	/// </para>
	/// </remarks>
	public void LineStrip(ReadOnlySpan<Vertex2dColor> verts, float thickness = 1f, LineJoin join = default, LineCap cap = default) {
		chkThickness(thickness);
		if (verts.Length < 2)
			throw new ArgumentException("a line strip needs at least 2 vertices", nameof(verts));
		stroke(o, verts, [], default, thickness, closed: false, join, cap);
	}

	/// <summary>
	/// Draws a flat-color line strip.
	/// </summary>
	/// <param name="points">Points forming the line strip.</param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="join">How the strip is drawn where two segments meet.</param>
	/// <param name="cap">How the strip is drawn at its first and last points.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="points"/> has fewer than 2 points.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <inheritdoc cref="LineStrip(ReadOnlySpan{Vertex2dColor}, float, LineJoin, LineCap)" path="/remarks"/>
	public void LineStrip(ReadOnlySpan<Vector2> points, SrgbColor32 color, float thickness = 1f, LineJoin join = default, LineCap cap = default) {
		chkThickness(thickness);
		if (points.Length < 2)
			throw new ArgumentException("a line strip needs at least 2 points", nameof(points));
		stroke(o, [], points, color, thickness, closed: false, join, cap);
	}

	/// <summary>
	/// Draws a closed line strip, i.e. one with a segment from the last point back to the first, with
	/// interpolated per-vertex colors.
	/// </summary>
	/// <param name="verts">Points forming the loop.</param>
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="join">How the loop is drawn where two segments meet.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="verts"/> has fewer than 2 vertices.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Draws like <see cref="LineStrip(ReadOnlySpan{Vertex2dColor}, float, LineJoin, LineCap)"/>,
	/// except that every point is a joint, including the first one. The last point may repeat the
	/// first one; it then counts as the same point.
	/// </para>
	/// <para>
	/// If only 2 distinct points remain, this draws the single segment between them, as an open strip
	/// with <see cref="LineCap.Butt"/> caps.
	/// </para>
	/// </remarks>
	public void LineLoop(ReadOnlySpan<Vertex2dColor> verts, float thickness = 1f, LineJoin join = default) {
		chkThickness(thickness);
		if (verts.Length < 2)
			throw new ArgumentException("a line loop needs at least 2 vertices", nameof(verts));
		stroke(o, verts, [], default, thickness, closed: true, join, default);
	}

	/// <summary>
	/// Draws a flat-color closed line strip, i.e. one with a segment from the last point back to the
	/// first.
	/// </summary>
	/// <param name="points">Points forming the loop.</param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="join">How the loop is drawn where two segments meet.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="points"/> has fewer than 2 points.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <inheritdoc cref="LineLoop(ReadOnlySpan{Vertex2dColor}, float, LineJoin)" path="/remarks"/>
	public void LineLoop(ReadOnlySpan<Vector2> points, SrgbColor32 color, float thickness = 1f, LineJoin join = default) {
		chkThickness(thickness);
		if (points.Length < 2)
			throw new ArgumentException("a line loop needs at least 2 points", nameof(points));
		stroke(o, [], points, color, thickness, closed: true, join, default);
	}

	// ==========================================================================
	// shapes

	/// <summary>
	/// Draws the outline of a rectangle.
	/// </summary>
	/// <param name="rect">Rectangle to outline.</param>
	/// <param name="color">Outline color.</param>
	/// <param name="thickness">Outline thickness in drawable units.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is not finite and positive.
	/// </exception>
	/// <remarks>
	/// The outline lies inside <paramref name="rect"/>, i.e. it covers the outermost drawable units
	/// of what <see cref="Rect(RectF, SrgbColor32)"/> would fill. If the outline is at least half as
	/// thick as the shorter side, it fills the whole rectangle.
	/// </remarks>
	public void RectOutline(RectF rect, SrgbColor32 color, float thickness = 1f) {
		chkThickness(thickness);
		roundedRectOutline(o, rect, 0f, color, thickness, 0);
	}

	/// <summary>
	/// Draws a filled circle.
	/// </summary>
	/// <param name="center">Center of the circle.</param>
	/// <param name="radius">Radius of the circle.</param>
	/// <param name="color">Fill color.</param>
	/// <param name="segments">
	/// Number of edges of the polygon approximating the circle, or <see langword="null"/> to pick one
	/// from the radius; see the remarks.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="radius"/> is not finite and non-negative, or if
	/// <paramref name="segments"/> is not between 3 and 65536.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Not a true circle; approximated with a regular polygon with <paramref name="segments"/> edges.
	/// The default number of edges is computed from the radius <b>after
	/// <see cref="CanvasParams.Transform"/> and <see cref="CanvasTarget.Viewport"/></b>, i.e. in
	/// target pixels, such that, for any edge of the polygon, the sagitta of the arc subtended by the
	/// edge is no more than 0.25 <b>target pixels</b> (or, in simpler terms, the polygon is at most
	/// 0.25px off from a true circle), and then clamped to at least 8 and at most 1024 edges. The
	/// polygon has a vertex at angle 0, i.e. at <c>center + (radius, 0)</c>.
	/// </para>
	/// <para>
	/// A radius of 0 draws nothing.
	/// </para>
	/// </remarks>
	public void Circle(Vector2 center, float radius, SrgbColor32 color, int? segments = null) {
		chkRadius(radius, nameof(radius));
		chkSegments(segments, 3);
		ellipse(o, center, new Vector2(radius), color, segments);
	}

	/// <summary>
	/// Draws a filled axis-aligned ellipse.
	/// </summary>
	/// <param name="center">Center of the ellipse.</param>
	/// <param name="radii">Radii along the X and Y axes.</param>
	/// <param name="color">Fill color.</param>
	/// <param name="segments">
	/// Number of edges of the polygon approximating the ellipse, or <see langword="null"/> to pick one
	/// from the larger radius; see the remarks.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if a component of <paramref name="radii"/> is not finite and non-negative, or if
	/// <paramref name="segments"/> is not between 3 and 65536.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The polygon is the "circle" polygon of <see cref="Circle(Vector2, float, SrgbColor32, int?)"/>,
	/// scaled along the axes, and gets the same default number of edges as a circle with the larger of
	/// the two radii. Rotated ellipses can be drawn via a transform matrix.
	/// </para>
	/// <para>
	/// A radius of 0 draws nothing.
	/// </para>
	/// </remarks>
	public void Ellipse(Vector2 center, Vector2 radii, SrgbColor32 color, int? segments = null) {
		chkRadius(radii.X, nameof(radii));
		chkRadius(radii.Y, nameof(radii));
		chkSegments(segments, 3);
		ellipse(o, center, radii, color, segments);
	}

	/// <summary>
	/// Draws the outline of a circle.
	/// </summary>
	/// <param name="center">Center of the circle.</param>
	/// <param name="radius">Radius of the circle.</param>
	/// <param name="color">Outline color.</param>
	/// <param name="thickness">Outline thickness in drawable units.</param>
	/// <param name="segments">
	/// Number of edges of the polygon approximating the circle, or <see langword="null"/> to pick one
	/// from the radius.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="radius"/> is not finite and non-negative, if
	/// <paramref name="thickness"/> is not finite and positive, or if <paramref name="segments"/> is
	/// not between 3 and 65536.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The outline lies inside the circle, i.e. it covers the outermost drawable units of what
	/// <see cref="Circle(Vector2, float, SrgbColor32, int?)"/> with the same arguments would fill. If
	/// the outline is at least as thick as the radius, it fills the whole circle.
	/// </para>
	/// <para>
	/// The "circle" polygon and its default number of edges are the same as for
	/// <see cref="Circle(Vector2, float, SrgbColor32, int?)"/>. A radius of 0 draws nothing.
	/// </para>
	/// </remarks>
	public void CircleOutline(Vector2 center, float radius, SrgbColor32 color, float thickness = 1f, int? segments = null) {
		chkRadius(radius, nameof(radius));
		chkThickness(thickness);
		chkSegments(segments, 3);
		ellipseOutline(o, center, new Vector2(radius), color, thickness, segments);
	}

	/// <summary>
	/// Draws the outline of an axis-aligned ellipse.
	/// </summary>
	/// <param name="center">Center of the ellipse.</param>
	/// <param name="radii">Radii along the X and Y axes.</param>
	/// <param name="color">Outline color.</param>
	/// <param name="thickness">Outline thickness in drawable units.</param>
	/// <param name="segments">
	/// Number of edges of the polygon approximating the ellipse, or <see langword="null"/> to pick one
	/// from the larger radius.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if a component of <paramref name="radii"/> is not finite and non-negative, if
	/// <paramref name="thickness"/> is not finite and positive, or if <paramref name="segments"/> is
	/// not between 3 and 65536.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The outline lies inside the ellipse: its inner edge is the ellipse with both radii reduced by
	/// <paramref name="thickness"/>. This isn't exactly a constant-thickness band for ellipses that
	/// aren't circles, but it's close to one unless the outline is thick compared to the radii. If the
	/// outline is at least as thick as the smaller radius, it fills the whole ellipse.
	/// </para>
	/// <para>
	/// The "circle" polygon and its default number of edges are the same as for
	/// <see cref="Ellipse(Vector2, Vector2, SrgbColor32, int?)"/>. A radius of 0 draws nothing.
	/// </para>
	/// </remarks>
	public void EllipseOutline(Vector2 center, Vector2 radii, SrgbColor32 color, float thickness = 1f, int? segments = null) {
		chkRadius(radii.X, nameof(radii));
		chkRadius(radii.Y, nameof(radii));
		chkThickness(thickness);
		chkSegments(segments, 3);
		ellipseOutline(o, center, radii, color, thickness, segments);
	}

	/// <summary>
	/// Draws a filled rectangle with rounded corners.
	/// </summary>
	/// <param name="rect">Rectangle to fill.</param>
	/// <param name="radius">Corner radius.</param>
	/// <param name="color">Fill color.</param>
	/// <param name="segments">
	/// Number of edges approximating each corner, or <see langword="null"/> to pick one from the
	/// radius.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="radius"/> is not finite and non-negative, or if
	/// <paramref name="segments"/> is not between 1 and 65536.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The radius is clamped to at most half of the shorter side; as such, such a large radius
	/// produces a pill shape or, for a square, a circle. A radius of 0 draws a plain rectangle.
	/// </para>
	/// <para>
	/// By default, the corners get as many edges as the matching quarter of
	/// <see cref="Circle(Vector2, float, SrgbColor32, int?)"/> would.
	/// </para>
	/// </remarks>
	public void RoundedRect(RectF rect, float radius, SrgbColor32 color, int? segments = null) {
		chkRadius(radius, nameof(radius));
		chkSegments(segments, 1);
		OwnedCanvas oc = o;
		radius = MathF.Min(radius, MathF.Min(rect.Width, rect.Height) * 0.5f);
		roundedRect(oc, rect, radius, color, cornerSegments(oc, radius, segments));
	}

	/// <summary>
	/// Draws the outline of a rectangle with rounded corners.
	/// </summary>
	/// <param name="rect">Rectangle to outline.</param>
	/// <param name="radius">Corner radius of the outer edge.</param>
	/// <param name="color">Outline color.</param>
	/// <param name="thickness">Outline thickness in drawable units.</param>
	/// <param name="segments">
	/// Number of edges approximating each corner, or <see langword="null"/> to pick one from the
	/// radius.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="radius"/> is not finite and non-negative, if
	/// <paramref name="thickness"/> is not finite and positive, or if <paramref name="segments"/> is
	/// not between 1 and 65536.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The outline lies inside the shape that
	/// <see cref="RoundedRect(RectF, float, SrgbColor32, int?)"/> with the same arguments would fill,
	/// and has a constant thickness: the inner edge's corner radius is <c>radius - thickness</c>, or
	/// sharp if that's not positive. If the outline is at least half as thick as the shorter side, it
	/// fills the whole shape.
	/// </para>
	/// <para>
	/// The radius limit and the default number of edges are the same as for
	/// <see cref="RoundedRect(RectF, float, SrgbColor32, int?)"/>.
	/// </para>
	/// </remarks>
	public void RoundedRectOutline(RectF rect, float radius, SrgbColor32 color, float thickness = 1f, int? segments = null) {
		chkRadius(radius, nameof(radius));
		chkThickness(thickness);
		chkSegments(segments, 1);
		OwnedCanvas oc = o;
		radius = MathF.Min(radius, MathF.Min(rect.Width, rect.Height) * 0.5f);
		roundedRectOutline(oc, rect, radius, color, thickness, cornerSegments(oc, radius, segments));
	}

	/// <summary>
	/// Draws a filled circular sector ("pie slice").
	/// </summary>
	/// <param name="center">Center of the circle.</param>
	/// <param name="radius">Radius of the circle.</param>
	/// <param name="startAngle">Angle where the sector starts, in radians.</param>
	/// <param name="sweepAngle">
	/// Angle the sector covers, in radians, starting from <paramref name="startAngle"/>.
	/// </param>
	/// <param name="color">Fill color.</param>
	/// <param name="segments">
	/// Number of edges approximating the curved edge, or <see langword="null"/> to pick one from the
	/// radius and <paramref name="sweepAngle"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="radius"/> is not finite and non-negative,
	/// <paramref name="startAngle"/> or <paramref name="sweepAngle"/> is not finite, or
	/// <paramref name="segments"/> is not between 1 and 65536.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Angle 0 points along +X, and positive angles turn toward +Y, i.e. visually clockwise, matching
	/// <see cref="Matrix3x2.CreateRotation(float)"/>. A negative <paramref name="sweepAngle"/> goes
	/// the other way. A sweep of a full turn or more draws the whole circle, as by
	/// <see cref="Circle(Vector2, float, SrgbColor32, int?)"/>.
	/// </para>
	/// <para>
	/// By default, the polygon approximating the curved edge gets as many edges as the matching part
	/// of <see cref="Circle(Vector2, float, SrgbColor32, int?)"/> would. A radius or sweep of 0 draws
	/// nothing.
	/// </para>
	/// </remarks>
	public void Pie(Vector2 center, float radius, float startAngle, float sweepAngle, SrgbColor32 color, int? segments = null) {
		chkRadius(radius, nameof(radius));
		chkAngle(startAngle, nameof(startAngle));
		chkAngle(sweepAngle, nameof(sweepAngle));
		chkSegments(segments, 1);
		OwnedCanvas oc = o;
		if (MathF.Abs(sweepAngle) >= MathF.Tau) {
			ellipse(oc, center, new Vector2(radius), color, segments);
			return;
		}
		if (radius == 0f || sweepAngle == 0f)
			return;
		normalizeSweep(ref startAngle, ref sweepAngle);
		int k = segments ?? arcSegments(oc, radius, sweepAngle);
		Span<Vector2> pts = oc.ScratchPoints(k + 2);
		pts[0] = center;
		arcPoints(pts[1..], center, new Vector2(radius), startAngle, sweepAngle / k);
		Span<uint> idxs = oc.ScratchIndices(k * 3);
		fanAroundCenter(idxs, k + 1, closed: false);
		oc.AddPrimitive(pts, color, idxs);
	}

	/// <summary>
	/// Draws a circular arc as a curved line.
	/// </summary>
	/// <param name="center">Center of the circle.</param>
	/// <param name="radius">Radius of the circle.</param>
	/// <param name="startAngle">Angle where the arc starts, in radians.</param>
	/// <param name="sweepAngle">
	/// Angle the arc covers, in radians, starting from <paramref name="startAngle"/>.
	/// </param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in drawable units.</param>
	/// <param name="cap">How the arc is drawn at both ends.</param>
	/// <param name="segments">
	/// Number of edges approximating the arc, or <see langword="null"/> to pick one from the radius
	/// and <paramref name="sweepAngle"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="radius"/> is not finite and non-negative,
	/// <paramref name="startAngle"/> or <paramref name="sweepAngle"/> is not finite,
	/// <paramref name="thickness"/> is not finite and positive, or <paramref name="segments"/> is not
	/// between 1 and 65536.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Unlike the outline methods, the line is centered on the circle, like
	/// <see cref="LineStrip(ReadOnlySpan{Vector2}, SrgbColor32, float, LineJoin, LineCap)"/> centers
	/// its line on the points. If the line is thicker than the diameter, its inner edge is clamped to
	/// the center.
	/// </para>
	/// <para>
	/// The caps attach to the ends, which run along the radius. If the inner edge is clamped, the ends
	/// are shorter than the thickness, and the caps shrink with them: a <see cref="LineCap.Square"/>
	/// cap extends by half the thickness, and a <see cref="LineCap.Round"/> is a half-disc over the
	/// end, so neither reaches past the center. A sweep of a full turn or more has no ends (see
	/// below), so <paramref name="cap"/> doesn't matter in that case. Caps of an arc that almost
	/// closes may overlap.
	/// </para>
	/// <para>
	/// Angle 0 points along +X, and positive angles turn toward +Y, i.e. visually clockwise, matching
	/// <see cref="Matrix3x2.CreateRotation(float)"/>. A negative <paramref name="sweepAngle"/> goes
	/// the other way. A sweep of a full turn or more draws a closed ring. By default, the arc gets as
	/// many edges as the matching part of <see cref="Circle(Vector2, float, SrgbColor32, int?)"/> with
	/// the outer radius would. A sweep of 0 draws nothing.
	/// </para>
	/// </remarks>
	public void Arc(Vector2 center, float radius, float startAngle, float sweepAngle, SrgbColor32 color, float thickness = 1f, LineCap cap = default, int? segments = null) {
		chkRadius(radius, nameof(radius));
		chkAngle(startAngle, nameof(startAngle));
		chkAngle(sweepAngle, nameof(sweepAngle));
		chkThickness(thickness);
		chkSegments(segments, 1);
		OwnedCanvas oc = o;
		if (sweepAngle == 0f)
			return;
		Vector2 outer = new(radius + thickness * 0.5f);
		Vector2 inner = new(MathF.Max(radius - thickness * 0.5f, 0f));
		if (MathF.Abs(sweepAngle) >= MathF.Tau) {
			ring(oc, center, outer, inner, 0f, MathF.Tau, segments ?? ellipseSegments(oc, outer.X), closed: true, color);
			return;
		}
		normalizeSweep(ref startAngle, ref sweepAngle);
		int segs = segments ?? arcSegments(oc, outer.X, sweepAngle);
		ring(oc, center, outer, inner, startAngle, sweepAngle, segs, closed: false, color);
		if (cap != LineCap.Butt) {
			// same angle math as arcPoints, so the caps' edges match the ring's ends
			arcCap(oc, center, outer.X, inner.X, startAngle, -1f, cap, color);
			arcCap(oc, center, outer.X, inner.X, startAngle + segs * (sweepAngle / segs), 1f, cap, color);
		}
	}

	// ==========================================================================
	// textures

	/// <summary>
	/// Draws a texture, or a region of it, optionally transformed and tinted.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="draw">
	/// Where to draw it, which part of it, how it's transformed, and with what tint. A
	/// <see cref="Vector2"/> (top-left position, natural size; see <see cref="TexDraw"/>) or
	/// <see cref="RectF"/> (destination rect) converts to this implicitly.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="draw"/>'s <see cref="TexDraw.Origin"/>, <see cref="TexDraw.Scale"/>,
	/// or <see cref="TexDraw.Rotation"/> isn't finite, or if <paramref name="tex"/> is a
	/// <see cref="Texture2dSlice"/> that doesn't fit in its texture or <paramref name="draw"/>'s
	/// <see cref="TexDraw.Source"/> reaches outside it.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target, if it's a
	/// <see cref="Versioned{T}"/> (or a slice of one) and the calling thread isn't attached to its
	/// asset store (see <see cref="DrawableTexture"/>), or if the current material is
	/// <see cref="TextureInterpretation.Sdf"/> and the sampling filter isn't
	/// <see cref="TexFilter.Linear"/>.
	/// </exception>
	public void Texture(DrawableTexture tex, in TexDraw draw) {
		// positive angles turn +X towards +Y, i.e. clockwise since +Y = down
		static Vector2 rot(Vector2 v, float sin, float cos) =>
			new(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);

		OwnedCanvas oc = o;
		ArgumentInvalidDefaultException.ThrowIfDefault(tex);
		ArgumentInvalidDefaultException.ThrowIfDefault(draw);
		if (!float.IsFinite(draw.Origin.X) || !float.IsFinite(draw.Origin.Y) || !float.IsFinite(draw.Scale.X) || !float.IsFinite(draw.Scale.Y) || !float.IsFinite(draw.Rotation))
			throw new ArgumentException("texture draw origin, scale, and rotation must be finite", nameof(draw));
		ResolvedDrawableTexture r = tex.Resolve();
		RectF uv;
		float w, h;
		if (tex.SliceRect is RectI s) {
			// can only fail for assets, whose size isn't known until borrowed
			if ((long)s.X + s.Width > r.Width || (long)s.Y + s.Height > r.Height)
				throw new ArgumentException($"texture slice {s} doesn't fit in its {r.Width}x{r.Height} texture", nameof(tex));
			if (!draw.Source.FitsIn(s.Width, s.Height))
				throw new ArgumentException("texture draw source region extends outside the texture slice", nameof(draw));
			// resolve relative to the slice, then map into the whole texture
			(RectF local, w, h) = draw.Source.Resolve((uint)s.Width, (uint)s.Height);
			float invW = 1f / r.Width, invH = 1f / r.Height;
			uv = new RectF(
				(s.X + local.X * s.Width) * invW,
				(s.Y + local.Y * s.Height) * invH,
				local.Width * s.Width * invW,
				local.Height * s.Height * invH
			);
		} else {
			(uv, w, h) = draw.Source.Resolve(r.Width, r.Height);
		}
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
			draw.Sampling ?? oc.CurrentParams.Sampling,
			[
				new Vertex2dTextureColor(rotTl.X, rotTl.Y, u0, v0, c),
				new Vertex2dTextureColor(rotTr.X, rotTr.Y, u1, v0, c),
				new Vertex2dTextureColor(rotBl.X, rotBl.Y, u0, v1, c),
				new Vertex2dTextureColor(rotBr.X, rotBr.Y, u1, v1, c),
			],
			quadIndices.AsSpan()
		);
	}

	// ==========================================================================
	// text

	/// <summary>
	/// Draws laid out text, running a relayout first if it's stale; see
	/// <see cref="TextLayout.IsStale"/>.
	/// </summary>
	/// <param name="layout">The text.</param>
	/// <param name="at">Position for the layout's top-left corner.</param>
	/// <param name="tint">
	/// Multiplicative tint for the glyphs' own colors (see <see cref="TextStyle.Color"/>), or
	/// <see langword="null"/> for <see cref="SrgbColor32.White"/>, i.e. no tint. Alpha is multiplied
	/// too, and the multiplication happens on the sRGB-encoded values, same as blending (see
	/// type-level docs).
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="layout"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the <see cref="TextSystem"/> that made <paramref name="layout"/> has been disposed.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if one of the layout's fonts is an asset and the calling thread isn't attached to its
	/// asset store.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Glyphs are rasterized for the size they end up at in target pixels, as given by the font
	/// size and <see cref="EffectiveTransform"/>, so text stays sharp under scaling.
	/// </para>
	/// <para>
	/// If <see cref="EffectiveTransform"/> only scales and translates, each glyph is snapped to the
	/// target pixel (not drawable unit) grid: the baseline is snapped to the nearest pixel row, and
	/// the horizontal position is snapped to the nearest quarter pixel, with the glyph rasterized at
	/// that quarter-pixel offset. Under rotation/shear/mirroring, glyphs are drawn where the layout
	/// puts them with no snapping.
	/// </para>
	/// <para>
	/// Draws with <see cref="CanvasMaterial.Rmask"/> and <see cref="TexFilter.Linear"/> sampling
	/// regardless of <see cref="CurrentParams"/>; the other parameters apply as usual.
	/// </para>
	/// </remarks>
	public void Text(TextLayout layout, Vector2 at, SrgbColor32? tint = null) {
		OwnedCanvas oc = o;
		ArgumentNullException.ThrowIfNull(layout);
		TextRenderer.Draw(oc, layout, at, tint ?? SrgbColor32.White);
	}

	/// <summary>
	/// Lays out and draws text with a single style, using the <see cref="TextSystem"/> this canvas was
	/// created with.
	/// </summary>
	/// <param name="text">
	/// The text. Each <c>\n</c> starts a new paragraph. Unpaired surrogates are replaced with U+FFFD
	/// REPLACEMENT CHARACTER.
	/// </param>
	/// <param name="style">The style of the whole text.</param>
	/// <param name="at">Position for the laid out text's top-left corner.</param>
	/// <param name="options">
	/// Line-breaking/alignment options, or <see langword="null"/> for
	/// <see cref="TextLayoutOptions.Default"/>.
	/// </param>
	/// <param name="tint">
	/// Multiplicative tint for the glyphs' own colors (see <see cref="TextStyle.Color"/>), or
	/// <see langword="null"/> for <see cref="SrgbColor32.White"/>, i.e. no tint. Alpha is multiplied
	/// too, and the multiplication happens on the sRGB-encoded values, same as blending (see
	/// type-level docs).
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="style"/>'s <see cref="TextStyle.Fonts"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="style"/>'s <see cref="TextStyle.Size"/> isn't positive and finite,
	/// or <paramref name="options"/>'s <see cref="TextLayoutOptions.MaxWidth"/> is negative or NaN.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the canvas's <see cref="TextSystem"/> has been disposed.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the canvas's <see cref="OwnedCanvas"/> was created without a
	/// <see cref="TextSystem"/>, or if one of <paramref name="style"/>'s fonts is an asset and the
	/// calling thread isn't attached to its asset store.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The layout is cached in the <see cref="TextSystem"/>, keyed by <paramref name="text"/>,
	/// <paramref name="style"/>, and <paramref name="options"/>, so drawing the same text every frame
	/// only lays out once and cache-hits afterward. A cached layout is dropped if it's been unused
	/// for <see cref="TextCacheOptions.LayoutCacheFrames"/> frames.
	/// </para>
	/// <para>
	/// Otherwise the same as <see cref="Text(TextLayout, Vector2, SrgbColor32?)"/>; see its docs.
	/// </para>
	/// </remarks>
	public void Text(ReadOnlySpan<char> text, in TextStyle style, Vector2 at, TextLayoutOptions? options = null, SrgbColor32? tint = null) {
		OwnedCanvas oc = o;
		TextStyle.Validate(in style, nameof(style));
		TextLayoutOptions opts = options ?? TextLayoutOptions.Default;
		TextLayoutOptions.Validate(in opts, nameof(options));
		TextRenderer.Draw(oc, textSystem(oc).GetCachedLayout(text, in style, in opts), at, tint ?? SrgbColor32.White);
	}

	/// <summary>
	/// Lays out and draws rich text, using the <see cref="TextSystem"/> this canvas was created with.
	/// </summary>
	/// <param name="text">The text and its styles.</param>
	/// <param name="at">Position for the laid out text's top-left corner.</param>
	/// <param name="options">
	/// Line-breaking/alignment options, or <see langword="null"/> for
	/// <see cref="TextLayoutOptions.Default"/>.
	/// </param>
	/// <param name="tint">
	/// Multiplicative tint for the glyphs' own colors (see <see cref="TextStyle.Color"/>), or
	/// <see langword="null"/> for <see cref="SrgbColor32.White"/>, i.e. no tint. Alpha is multiplied
	/// too, and the multiplication happens on the sRGB-encoded values, same as blending (see
	/// type-level docs).
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="text"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="options"/>'s <see cref="TextLayoutOptions.MaxWidth"/> is negative or
	/// NaN.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the canvas's <see cref="TextSystem"/> has been disposed.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the canvas's <see cref="OwnedCanvas"/> was created without a
	/// <see cref="TextSystem"/>, or if one of the text's fonts is an asset and the calling thread
	/// isn't attached to its asset store.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The layout is cached in the <see cref="TextSystem"/>, keyed by <paramref name="text"/>, which
	/// compares by value (see <see cref="RichText"/>), and <paramref name="options"/>, so drawing equal
	/// text every frame only lays out once, even if it's built again each time. A cached layout is
	/// dropped if it's been unused for <see cref="TextCacheOptions.LayoutCacheFrames"/> frames.
	/// </para>
	/// <para>
	/// Otherwise the same as <see cref="Text(TextLayout, Vector2, SrgbColor32?)"/>; see its docs.
	/// </para>
	/// </remarks>
	public void Text(RichText text, Vector2 at, TextLayoutOptions? options = null, SrgbColor32? tint = null) {
		OwnedCanvas oc = o;
		ArgumentNullException.ThrowIfNull(text);
		TextLayoutOptions opts = options ?? TextLayoutOptions.Default;
		TextLayoutOptions.Validate(in opts, nameof(options));
		TextRenderer.Draw(oc, textSystem(oc).GetCachedLayout(text, in opts), at, tint ?? SrgbColor32.White);
	}

	private static TextSystem textSystem(OwnedCanvas oc) =>
		oc.TextSystem ?? throw new InvalidOperationException("this canvas has no TextSystem, since its OwnedCanvas was created without one");

	// ==========================================================================
	// params

	/// <summary>
	/// Pushes an override onto the params stack.
	/// </summary>
	/// <param name="ov">Partial override applied on top of <see cref="CurrentParams"/>.</param>
	/// <returns>
	/// A scope that restores the previous parameters when disposed. Disposing it while a later
	/// override is still pushed throws <see cref="InvalidOperationException"/>.
	/// </returns>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if the resulting parameters' target or material is the <see langword="default"/> value.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the resulting parameters are invalid: they target a texture with an sRGB format, or
	/// their transform or effective transform isn't invertible (see <see cref="MatrixInvert"/>).
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the resulting parameters target the primary output and the frame has none.
	/// </exception>
	/// <remarks>
	/// <para>
	/// This is the standard way to temporarily change the transform, target, etc. Changing the target
	/// or color attachment ops ends the current render pass; see <see cref="CanvasParams"/>.
	/// </para>
	/// <para>
	/// The pushed parameters are <c>CurrentParams.Merge(ov)</c>; see
	/// <see cref="CanvasParams.Merge(in CanvasParamsOverride)"/>.
	/// </para>
	/// </remarks>
	public CanvasParamsScope PushParams(scoped in CanvasParamsOverride ov) => o.PushParams(in ov);

	/// <summary>
	/// Pushes an override onto the params stack.
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
		CanvasScissorOverride? scissor = null,
		CanvasTransformOverride? transform = null,
		CanvasOutputState? outputState = null,
		CanvasMaterial? material = null,
		TexSampling? sampling = null
	) {
		ArgumentInvalidDefaultException.ThrowIfDefault(target);
		ArgumentInvalidDefaultException.ThrowIfDefault(colorAttachmentOps);
		ArgumentInvalidDefaultException.ThrowIfDefault(scissor);
		ArgumentInvalidDefaultException.ThrowIfDefault(transform);
		ArgumentInvalidDefaultException.ThrowIfDefault(material);
		return o.PushParams(new CanvasParamsOverride(target, colorAttachmentOps, scissor, transform, outputState, material, sampling));
	}

	/// <summary>
	/// Pushes an override onto the params stack, or does nothing and returns a no-op scope if
	/// <paramref name="ov"/> is <see langword="null"/>.
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
	/// Lets APIs return <c>CanvasParamsOverride?</c>, e.g.
	/// <c>using (cv.PushParamsIfNonnull(foo.GetCanvasParams(bar, baz))) { ... }</c>.
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

	/// <summary>
	/// &gt;0 = ccw, &lt;0 = cw.
	/// </summary>
	/// <remarks>
	/// The above holds in cartesian coordinates, where +Y = up, and holds here too, as +Y being down
	/// is because of the ortho projection, and +Y in clip space points up like in cartesian.
	/// </remarks>
	private static float winding(Vector2 a, Vector2 b, Vector2 c) =>
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
		if (indexCount <= maxStackIndices) {
			Span<uint> idxs = stackalloc uint[indexCount];
			gen(idxs, verts.Length);
			add(oc, verts, idxs, state);
		} else {
			uint[] rented = ArrayPool<uint>.Shared.Rent(indexCount);
			try {
				Span<uint> idxs = rented.AsSpan(0, indexCount);
				gen(idxs, verts.Length);
				add(oc, verts, idxs, state);
			} finally {
				ArrayPool<uint>.Shared.Return(rented);
			}
		}
	}

	// ==========================================================================
	// shape helpers

	private const int minAutoSegments = 8;
	private const int maxAutoSegments = 1024;
	private const int maxSegments = 65536;

	/// <summary>
	/// Longest miter <see cref="LineJoin.Miter"/> draws before it becomes a bevel, measured from the
	/// joint to the miter's tip, in multiples of half the thickness. 4 matches SVG's default
	/// <c>stroke-miterlimit</c>, and switches at roughly 29 degrees.
	/// </summary>
	private const float miterLimit = 4f;

	private static void chkRadius(float radius, string paramName) {
		// written so that NaN fails too
		if (!(radius >= 0f) || !float.IsFinite(radius))
			throw new ArgumentOutOfRangeException(paramName, radius, "radius must be finite and non-negative");
	}

	private static void chkAngle(float angle, string paramName) {
		if (!float.IsFinite(angle))
			throw new ArgumentOutOfRangeException(paramName, angle, "angle must be finite");
	}

	private static void chkSegments(int? segments, int min) {
		if (segments is int s && (s < min || s > maxSegments))
			throw new ArgumentOutOfRangeException(nameof(segments), s, $"segment count must be between {min} and {maxSegments}");
	}

	/// <summary>
	/// Computes the automatic edge count for an ellipse whose larger radius is <paramref name="r"/>,
	/// such that the sagitta of the arc subtended by any edge of the polygon is no more than 0.25
	/// target pixels, then clamps to [<see cref="minAutoSegments"/>, <see cref="maxAutoSegments"/>].
	/// </summary>
	/// <remarks>
	/// The transform's scale is taken as the length of the longer of the transformed unit axes, which
	/// is exact unless the transform shears; if it does shear, may underestimate the largest stretch
	/// by up to a factor of sqrt(2).
	/// </remarks>
	private static int ellipseSegments(OwnedCanvas oc, float r) {
		const float tolerance = 0.25f;
		Matrix3x2 m = oc.EffectiveTransform;
		float scale = MathF.Sqrt(MathF.Max(m.M11 * m.M11 + m.M12 * m.M12, m.M21 * m.M21 + m.M22 * m.M22));
		float rpx = r * scale;
		if (!(rpx > tolerance))
			return minAutoSegments;
		// an edge spanning 2pi/n has sagitta r(1 - cos(pi/n)); huge radii give acos(1) = 0, i.e. +inf
		float n = MathF.Ceiling(MathF.PI / MathF.Acos(1f - tolerance / rpx));
		return float.IsNaN(n) ? maxAutoSegments : (int)Math.Clamp(n, minAutoSegments, maxAutoSegments);
	}

	/// <summary>
	/// Computes the automatic edge count for an arc of radius <paramref name="r"/> covering
	/// <paramref name="sweep"/> radians (assumed to be non-negative). The formula is the matching
	/// fraction of <see cref="ellipseSegments"/>, rounded towards positive infinity and clamped to be
	/// at least 1.
	/// </summary>
	private static int arcSegments(OwnedCanvas oc, float r, float sweep) =>
		Math.Max(1, (int)MathF.Ceiling(ellipseSegments(oc, r) * (sweep / MathF.Tau)));

	/// <summary>
	/// Computes the automatic edge count for each corner of a rounded rect. The formula is 0 for sharp
	/// corners (radius 0, where <see cref="roundedRectPoints"/> emits only the corner point), else
	/// <paramref name="segments"/> if that's non-null, else a quarter-turn <see cref="arcSegments"/>.
	/// </summary>
	private static int cornerSegments(OwnedCanvas oc, float radius, int? segments) =>
		radius <= 0f ? 0 : segments ?? arcSegments(oc, radius, MathF.PI * 0.5f);

	/// <summary>
	/// Normalizes a negative sweep into a positive sweep producing the same arc.
	/// </summary>
	/// <remarks>
	/// Needed because <see cref="fanAroundCenter"/> and <see cref="ringIdxs"/> rely on points being in
	/// increasing angle order for winding.
	/// </remarks>
	private static void normalizeSweep(ref float start, ref float sweep) {
		if (sweep < 0f) {
			start += sweep;
			sweep = -sweep;
		}
	}

	/// <summary>
	/// Fills <paramref name="dst"/> with points on an axis-aligned ellipse, with point <c>i</c> being
	/// at angle <c>start + i * step</c>.
	/// </summary>
	/// <remarks>
	/// <see cref="arcCap"/> recomputes the arc's end points with the same expressions, so that its
	/// edges match the ring's ends exactly; both must be kept in sync.
	/// </remarks>
	private static void arcPoints(Span<Vector2> dst, Vector2 center, Vector2 radii, float start, float step) {
		for (int i = 0; i < dst.Length; i++) {
			(float sin, float cos) = MathF.SinCos(start + i * step);
			dst[i] = center + radii * new Vector2(cos, sin);
		}
	}

	/// <summary>
	/// Fills <paramref name="dst"/> with the outline of a rounded rect, as <c>4 * (k + 1)</c> points
	/// in increasing angle order, starting at the bottom right corner. Produces <c>k + 1</c> points
	/// per corner arc.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <paramref name="radius"/> must already be clamped to half the shorter side.
	/// </para>
	/// <para>
	/// If <paramref name="k"/> = 0, each corner is the single point at its arc's start, which is only
	/// meaningful in combination with <paramref name="radius"/> = 0, where that point is the rect's
	/// corner.
	/// </para>
	/// <para>
	/// With radius 0 and positive <paramref name="k"/>, each corner is <c>k + 1</c> copies of the
	/// same point, which <see cref="roundedRectOutline"/> uses for sharp inner corners.
	/// </para>
	/// </remarks>
	/// <exception cref="InternalStateException">
	/// Thrown if <paramref name="dst"/>'s length isn't exactly <c>4 * (k + 1)</c>.
	/// </exception>
	private static void roundedRectPoints(Span<Vector2> dst, float left, float top, float right, float bottom, float radius, int k) {
		if (dst.Length != 4 * (k + 1))
			throw new InternalStateException($"wrong dst point count {dst.Length}, expected {4 * (k + 1)}");
		float step = k == 0 ? 0f : MathF.PI * 0.5f / k;
		int j = k + 1;
		arcPoints(dst[..j], new Vector2(right - radius, bottom - radius), new Vector2(radius), 0f, step);
		arcPoints(dst[j..(2 * j)], new Vector2(left + radius, bottom - radius), new Vector2(radius), MathF.PI * 0.5f, step);
		arcPoints(dst[(2 * j)..(3 * j)], new Vector2(left + radius, top + radius), new Vector2(radius), MathF.PI, step);
		arcPoints(dst[(3 * j)..], new Vector2(right - radius, top + radius), new Vector2(radius), MathF.PI * 1.5f, step);
	}

	/// <summary>
	/// Fills a rounded rect, as a fan from its center, with <paramref name="k"/> edges per corner (see
	/// <see cref="cornerSegments"/>). <paramref name="radius"/> must already be clamped.
	/// </summary>
	private static void roundedRect(OwnedCanvas oc, RectF rect, float radius, SrgbColor32 color, int k) {
		int n = 4 * (k + 1);
		Span<Vector2> pts = oc.ScratchPoints(1 + n);
		pts[0] = new Vector2(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
		roundedRectPoints(pts[1..], rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height, radius, k);
		Span<uint> idxs = oc.ScratchIndices(n * 3);
		fanAroundCenter(idxs, n, closed: true);
		oc.AddPrimitive(pts, color, idxs);
	}

	/// <summary>
	/// Draws the inside outline of a rounded rect, as a ring between its outline and the outline of
	/// the rect inset by <paramref name="thickness"/>, with corner radius <c>radius - thickness</c>
	/// (sharp if not positive). <paramref name="radius"/> must already be clamped. Radius 0 and
	/// <paramref name="k"/> 0 produce a plain rect outline, as used by <see cref="RectOutline"/>.
	/// </summary>
	/// <remarks>
	/// Both outlines have the same point count and angle at each index, which the ring relies on.
	/// Falls back to <see cref="roundedRect"/> if the thickness is high enough that there wouldn't be
	/// a hole in the produced shape anymore.
	/// </remarks>
	private static void roundedRectOutline(OwnedCanvas oc, RectF rect, float radius, SrgbColor32 color, float thickness, int k) {
		if (thickness >= MathF.Min(rect.Width, rect.Height) * 0.5f) {
			roundedRect(oc, rect, radius, color, k);
			return;
		}
		float l = rect.X, t = rect.Y, r = rect.X + rect.Width, b = rect.Y + rect.Height;
		float th = thickness;
		int n = 4 * (k + 1);
		Span<Vector2> pts = oc.ScratchPoints(2 * n);
		roundedRectPoints(pts[..n], l, t, r, b, radius, k);
		// with radius >= thickness, the inner corners share the outer ones' centers
		roundedRectPoints(pts[n..], l + th, t + th, r - th, b - th, MathF.Max(radius - th, 0f), k);
		Span<uint> idxs = oc.ScratchIndices(n * 6);
		ringIdxs(idxs, n, closed: true);
		oc.AddPrimitive(pts, color, idxs);
	}

	/// <summary>
	/// Fills an axis-aligned ellipse, as a fan from its center, with <paramref name="segments"/> edges
	/// if that's non-null, else <see cref="ellipseSegments"/>. Draws nothing if either of the two
	/// <paramref name="radii"/> is zero.
	/// </summary>
	private static void ellipse(OwnedCanvas oc, Vector2 center, Vector2 radii, SrgbColor32 color, int? segments) {
		if (radii.X == 0f || radii.Y == 0f)
			return;
		int n = segments ?? ellipseSegments(oc, MathF.Max(radii.X, radii.Y));
		Span<Vector2> pts = oc.ScratchPoints(n + 1);
		pts[0] = center;
		arcPoints(pts[1..], center, radii, 0f, MathF.Tau / n);
		Span<uint> idxs = oc.ScratchIndices(n * 3);
		fanAroundCenter(idxs, n, closed: true);
		oc.AddPrimitive(pts, color, idxs);
	}

	/// <summary>
	/// Draws the inside outline of an axis-aligned ellipse, as a ring between it and the ellipse with
	/// both radii reduced by <paramref name="thickness"/>. Draws nothing if either of the two
	/// <paramref name="radii"/> is zero. Falls back to <see cref="ellipse"/> if the thickness is high
	/// enough that there wouldn't be a hole in the produced shape anymore.
	/// </summary>
	private static void ellipseOutline(OwnedCanvas oc, Vector2 center, Vector2 radii, SrgbColor32 color, float thickness, int? segments) {
		if (radii.X == 0f || radii.Y == 0f)
			return;
		if (thickness >= MathF.Min(radii.X, radii.Y)) {
			ellipse(oc, center, radii, color, segments);
			return;
		}
		int n = segments ?? ellipseSegments(oc, MathF.Max(radii.X, radii.Y));
		ring(oc, center, radii, radii - new Vector2(thickness), 0f, MathF.Tau, n, closed: true, color);
	}

	/// <summary>
	/// Fills the band between two axis-aligned elliptical arcs with radii <paramref name="outer"/> and
	/// <paramref name="inner"/> around <paramref name="center"/>, from <paramref name="start"/> over
	/// <paramref name="sweep"/> radians (assumed to be non-negative), with <paramref name="segs"/>
	/// edges.
	/// </summary>
	/// <remarks>
	/// If <paramref name="closed"/>, the band is a full ring and <paramref name="sweep"/> must be a
	/// full turn; the last point is then left out as it would repeat the first. An inner radius of 0
	/// is allowed and produces degenerate triangles at the center rather than overlap.
	/// </remarks>
	private static void ring(OwnedCanvas oc, Vector2 center, Vector2 outer, Vector2 inner, float start, float sweep, int segs, bool closed, SrgbColor32 color) {
		int n = closed ? segs : segs + 1;
		Span<Vector2> pts = oc.ScratchPoints(2 * n);
		arcPoints(pts[..n], center, outer, start, sweep / segs);
		arcPoints(pts[n..], center, inner, start, sweep / segs);
		Span<uint> idxs = oc.ScratchIndices(segs * 6);
		ringIdxs(idxs, n, closed);
		oc.AddPrimitive(pts, color, idxs);
	}

	/// <summary>
	/// Draws a <see cref="LineCap.Square"/> or <see cref="LineCap.Round"/> cap on the end of an arc
	/// drawn by <see cref="ring"/>, at <paramref name="angle"/>, extending along the arc's direction
	/// of increasing angle if <paramref name="dir"/> = 1, or against it if -1.
	/// </summary>
	/// <remarks>
	/// The cap is sized based on the end itself (the edge from the inner to the outer radius) rather
	/// than the thickness, so that it properly shrinks if the end's inner radius was clamped to 0. A
	/// square cap extends by half of the end's length, and a round cap is a half-disc with the end as
	/// its diameter.
	/// </remarks>
	/// <exception cref="InternalStateException">
	/// Thrown if <paramref name="cap"/> is <see cref="LineCap.Butt"/>.
	/// </exception>
	private static void arcCap(OwnedCanvas oc, Vector2 center, float outerRadius, float innerRadius, float angle, float dir, LineCap cap, SrgbColor32 color) {
		(float sin, float cos) = MathF.SinCos(angle);
		Vector2 o = center + new Vector2(outerRadius) * new Vector2(cos, sin);
		Vector2 i = center + new Vector2(innerRadius) * new Vector2(cos, sin);
		float e = (outerRadius - innerRadius) * 0.5f;
		Vector2 outward = dir * new Vector2(-sin, cos);
		if (cap == LineCap.Square) {
			Span<Vector2> quad = oc.ScratchPoints(4);
			quad[0] = o;
			quad[1] = i;
			quad[2] = i + outward * e;
			quad[3] = o + outward * e;
			oc.AddPrimitive(
				quad,
				color,
				winding(quad[0], quad[1], quad[2]) > 0f ? [0, 2, 1, 0, 3, 2] : [0, 1, 2, 0, 2, 3]
			);
			return;
		}
		if (cap != LineCap.Round)
			throw new InternalStateException("expected Square or Round cap for arcCap");
		// a half-disc over the end, from o around through outward to i
		int k = arcSegments(oc, e, MathF.PI);
		Span<Vector2> pts = oc.ScratchPoints(k + 2);
		pts[0] = (o + i) * 0.5f;
		arcPoints(pts[1..], pts[0], new Vector2(e), angle, dir * MathF.PI / k);
		pts[1] = o;
		pts[k + 1] = i;
		Span<uint> idxs = oc.ScratchIndices(k * 3);
		fanAroundCenter(idxs, k + 1, closed: false);
		if (dir < 0f) // the fan is cw
			for (int t = 0; t < idxs.Length; t += 3)
				(idxs[t + 1], idxs[t + 2]) = (idxs[t + 2], idxs[t + 1]);
		oc.AddPrimitive(pts, color, idxs);
	}

	/// <summary>
	/// Writes the indices of triangles from vertex 0 to each edge of the <paramref name="n"/>-point
	/// path at vertices 1 to <paramref name="n"/>. <paramref name="closed"/> additionally includes
	/// the edge from the last point back to the first, increasing the number of written indices from
	/// <c>3 * (n - 1)</c> to <c>3 * n</c>.
	/// </summary>
	/// <remarks>
	/// The triangles are ccw if the path goes in increasing angle order around vertex 0 (clockwise).
	/// </remarks>
	private static void fanAroundCenter(Span<uint> dst, int n, bool closed) {
		int edges = closed ? n : n - 1;
		for (int i = 0, j = 0; i < edges; i++) {
			dst[j++] = 0;
			dst[j++] = (uint)((i + 1) % n + 1);
			dst[j++] = (uint)(i + 1);
		}
	}

	/// <summary>
	/// Writes the indices of quads between an outer <paramref name="n"/>-point path at vertices 0 to
	/// <c>n - 1</c> and an inner one at <c>n</c> to <c>2n - 1</c>, joining the points with the same
	/// index. Produces <c>6 * n</c> indices if <paramref name="closed"/>, else <c>6 * (n - 1)</c>.
	/// </summary>
	/// <remarks>
	/// The triangles are ccw if both paths go in increasing angle order around the center they share.
	/// </remarks>
	private static void ringIdxs(Span<uint> dst, int n, bool closed) {
		int edges = closed ? n : n - 1;
		for (int i = 0, j = 0; i < edges; i++) {
			uint o0 = (uint)i;
			uint o1 = (uint)((i + 1) % n);
			uint i0 = o0 + (uint)n;
			uint i1 = o1 + (uint)n;
			dst[j++] = o0;
			dst[j++] = i0;
			dst[j++] = o1;
			dst[j++] = o1;
			dst[j++] = i0;
			dst[j++] = i1;
		}
	}

	/// <summary>
	/// <paramref name="d"/> turned a quarter turn toward increasing angles, i.e. clockwise.
	/// </summary>
	private static Vector2 perp(Vector2 d) => new(-d.Y, d.X);

	/// <summary>
	/// Draws a line strip, or a loop if <paramref name="closed"/>, from either
	/// <paramref name="verts"/>, or <paramref name="points"/> + <paramref name="color"/> if
	/// <paramref name="verts"/> is empty.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each point becomes a <i>row</i> of vertices, on the pos and neg sides of the line (i.e. along
	/// and against <see cref="perp"/> of the segment's direction, respectively), plus the vertices of
	/// its join/cap. Each segment is the quad between its start point's row and its end point's row,
	/// so neighboring segments share an edge rather than overlapping; join/cap geometry attaches to
	/// rows in the same way.
	/// </para>
	/// <para>
	/// A row has two pos/neg pairs: the "end" pair, where the arriving segment ends, and the "start"
	/// pair, where the leaving one starts. They differ only on the outer side of a bevel or round
	/// join, whose geometry sits between them; the inner corner is shared, and at ends and miters,
	/// both pairs are the same.
	/// </para>
	/// </remarks>
	private static void stroke(OwnedCanvas oc, ReadOnlySpan<Vertex2dColor> verts, ReadOnlySpan<Vector2> points, SrgbColor32 color, float thickness, bool closed, LineJoin join, LineCap cap) {
		// position of input point i, from whichever span is in use
		static Vector2 pos(ReadOnlySpan<Vertex2dColor> verts, ReadOnlySpan<Vector2> points, int i) =>
			verts.IsEmpty ? points[i] : (Vector2)verts[i];

		const float epsilon = 1e-4f;
		float h = thickness * 0.5f;
		bool flat = verts.IsEmpty;
		int count = flat ? points.Length : verts.Length;

		// drop points that (almost) coincide with the previous one, so every segment has a direction
		// kept[..n] are the indices of the remaining input points
		Span<int> kept = oc.ScratchInts(count);
		int n = 0;
		for (int i = 0; i < count; i++)
			if (n == 0 || Vector2.Distance(pos(verts, points, kept[n - 1]), pos(verts, points, i)) >= epsilon)
				kept[n++] = i;
		// a loop may repeat its first point at the end, which the closing segment covers anyway
		if (closed)
			while (n > 1 && Vector2.Distance(pos(verts, points, kept[n - 1]), pos(verts, points, kept[0])) < epsilon)
				n--;
		if (n < 2)
			return;
		// a loop of 2 points would draw the same segment twice
		if (n < 3)
			closed = false;

		bool roundCaps = !closed && cap == LineCap.Round;
		// edges of a half-turn of round join or cap; no join or cap turns further than that
		int kRound = join == LineJoin.Round || roundCaps ? arcSegments(oc, h, MathF.PI) : 0;

		// upper bounds per point:

		// a point's row with the join/cap is at most 3 verts with non-round geometry, or kRound + 2 with
		// round geometry (the arc's interior points + the inner corner or the cap's center)
		Span<Vertex2dColor> outVerts = oc.ScratchVerts(n * Math.Max(3, kRound + 2));

		// and has at most 6 indices for the segment arriving there, plus either 3 for a bevel or
		// 3 * kRound for round geometry; a point never gets both
		Span<uint> idxs = oc.ScratchIndices(n * (6 + Math.Max(3, 3 * kRound)));

		int vc = 0;
		int ic = 0;
		uint prevPos = 0, prevNeg = 0; // the previous row, as the start of the next segment
		uint firstPos = 0, firstNeg = 0; // the first row, as the end of the closing segment
		for (int k = 0; k < n; k++) {
			Vector2 p = pos(verts, points, kept[k]);
			SrgbColor32 c = flat ? color : verts[kept[k]].Color;

			// whether a segment arrives at / leaves from this point; if either is false, it's an end
			bool hasPrev = closed || k > 0;
			bool hasNext = closed || k < n - 1;

			// unit directions and lengths of the arriving and leaving segments, where they exist
			Vector2 dPrev = default, dNext = default;
			float lenPrev = 0f, lenNext = 0f;
			if (hasPrev) {
				Vector2 d = p - pos(verts, points, kept[(k + n - 1) % n]);
				lenPrev = d.Length(); // ensured nonzero by the dedup above
				dPrev = d / lenPrev;
			}
			if (hasNext) {
				Vector2 d = pos(verts, points, kept[(k + 1) % n]) - p;
				lenNext = d.Length(); // ensured nonzero by the dedup above
				dNext = d / lenNext;
			}

			// this row's vertices, as the end of the arriving segment and the start of the leaving one
			uint endPos, endNeg, startPos, startNeg;
			if (!hasPrev || !hasNext) {
				// an end: a single pair across the line, which both start and end use (only one of them
				// gets a segment)

				Vector2 nrm = perp(hasPrev ? dPrev : dNext) * h;
				// pointing away from the line, past the end
				Vector2 outward = hasPrev ? dPrev : -dNext;
				// square caps just move the pair outward, so the segment's quad covers the cap too
				Vector2 q = cap == LineCap.Square ? p + outward * h : p;
				endPos = startPos = (uint)vc;
				outVerts[vc++] = new Vertex2dColor(q + nrm, c);
				endNeg = startNeg = (uint)vc;
				outVerts[vc++] = new Vertex2dColor(q - nrm, c);

				if (roundCaps) {
					// a half-disc fan around p, from the pos vertex around the outside to the neg
					// one. starting at nrm's angle, sweeping +pi passes through -dNext at a start,
					// and sweeping -pi passes through dPrev at an end
					uint center = (uint)vc;
					outVerts[vc++] = new Vertex2dColor(p, c);
					roundFan(outVerts, ref vc, idxs, ref ic, center, startPos, startNeg, p, h, MathF.Atan2(nrm.Y, nrm.X), hasPrev ? -MathF.PI : MathF.PI, kRound, c);
				}
			} else {
				// a joint. n0 and n1 are the normals of the arriving and leaving segments respectively;
				// their offset lines meet at the miter point on the outer side of the turn, and at the
				// inner corner on the inner side

				Vector2 n0 = perp(dPrev);
				Vector2 n1 = perp(dNext);

				// +1 if the outer side of the turn is the pos side; a positive cross product means
				// the strip turns toward the pos side, making that the inner side. straight and
				// reversing joints count as turning toward the neg side, which works for both
				float side = dPrev.X * dNext.Y - dPrev.Y * dNext.X > 0f ? -1f : 1f;
				Vector2 sum = n0 + n1;
				float sumLen = sum.Length();
				Vector2 m = Vector2.Zero; // bisector of the normals
				float miterLen = float.PositiveInfinity;
				float innerLen = 0f; // stays 0 if the strip turns straight back
				if (sumLen >= epsilon) {
					m = sum / sumLen;
					// cos of half the angle between the normals is sumLen / 2
					miterLen = 2f * h / sumLen;
					// keep the inner corner from reaching past the neighboring points at sharp joins
					float along = MathF.Abs(Vector2.Dot(m, dPrev));
					innerLen = along > epsilon ? MathF.Min(miterLen, MathF.Min(lenPrev, lenNext) / along) : miterLen;
				}

				// the inner corner is always a single vertex shared by both segments, on the bisector
				uint inner = (uint)vc;
				outVerts[vc++] = new Vertex2dColor(p - side * innerLen * m, c);
				// the outer corner, as the arriving segment's and the leaving segment's outer vertex;
				// the same vertex for a miter, and the two ends of the bevel/round join otherwise
				uint outer0, outer1;

				// straight or almost straight joints always get the miter
				bool straight = miterLen <= h * (1f + epsilon);

				if (straight || (join == LineJoin.Miter && miterLen <= miterLimit * h)) {
					outer0 = outer1 = (uint)vc;
					outVerts[vc++] = new Vertex2dColor(p + side * miterLen * m, c);
				} else {
					// where each segment's outer edge would end with a butt cap at p
					outer0 = (uint)vc;
					outVerts[vc++] = new Vertex2dColor(p + side * h * n0, c);
					outer1 = (uint)vc;
					outVerts[vc++] = new Vertex2dColor(p + side * h * n1, c);
					if (join == LineJoin.Round) {
						// the area between both segments' quads is convex, so a fan from the
						// inner corner covers it exactly once
						// u0 and u1 point from p to outer0 and outer1; the arc between them
						// turns by the signed angle between them, which is at most a half-turn
						// in either way
						Vector2 u0 = side * n0;
						Vector2 u1 = side * n1;
						float turn = MathF.Atan2(u0.X * u1.Y - u0.Y * u1.X, Vector2.Dot(u0, u1));
						// the same edge density as a half-turn's kRound
						// clamp since a strip turning straight back has |turn| = pi, for which
						// float rounding can push this past kRound
						int kj = Math.Clamp((int)MathF.Ceiling(kRound * MathF.Abs(turn) / MathF.PI), 1, kRound);
						roundFan(outVerts, ref vc, idxs, ref ic, inner, outer0, outer1, p, h, MathF.Atan2(u0.Y, u0.X), turn, kj, c);
					} else {
						// the bevel
						tri(outVerts, idxs, ref ic, outer0, outer1, inner);
					}
				}
				// map outer/inner onto pos/neg
				if (side > 0f) {
					(endPos, startPos, endNeg, startNeg) = (outer0, outer1, inner, inner);
				} else {
					(endPos, startPos, endNeg, startNeg) = (inner, inner, outer0, outer1);
				}
			}

			// the segment arriving here goes from the previous row's start pair to this row's end
			// pair; the first row's end pair waits for the closing segment of a loop
			if (k == 0) {
				firstPos = endPos;
				firstNeg = endNeg;
			} else {
				segmentQuad(idxs, ref ic, prevPos, prevNeg, endPos, endNeg);
			}
			prevPos = startPos;
			prevNeg = startNeg;
		}
		if (closed)
			segmentQuad(idxs, ref ic, prevPos, prevNeg, firstPos, firstNeg);
		oc.AddPrimitive(outVerts[..vc], idxs[..ic]);
	}

	/// <summary>
	/// Adds a fan of <paramref name="k"/> triangles from <paramref name="hub"/> over the circular arc
	/// of <paramref name="radius"/> around <paramref name="center"/>, from
	/// <paramref name="startAngle"/> over <paramref name="sweep"/> radians (either sign).
	/// </summary>
	/// <remarks>
	/// The arc's end points must already be vertices, <paramref name="first"/> and
	/// <paramref name="last"/>, so that they're shared with the geometry the fan attaches to; this
	/// adds the <c>k - 1</c> points between them.
	/// </remarks>
	private static void roundFan(Span<Vertex2dColor> verts, ref int vc, Span<uint> idx, ref int ic, uint hub, uint first, uint last, Vector2 center, float radius, float startAngle, float sweep, int k, SrgbColor32 color) {
		float step = sweep / k;
		uint prev = first;
		for (int i = 1; i < k; i++) {
			(float sin, float cos) = MathF.SinCos(startAngle + i * step);
			uint cur = (uint)vc;
			verts[vc++] = new Vertex2dColor(center + radius * new Vector2(cos, sin), color);
			tri(verts, idx, ref ic, hub, prev, cur);
			prev = cur;
		}
		tri(verts, idx, ref ic, hub, prev, last);
	}

	/// <summary>
	/// Adds a triangle, swapping <paramref name="b"/> and <paramref name="c"/> to make it ccw if it
	/// isn't already.
	/// </summary>
	private static void tri(ReadOnlySpan<Vertex2dColor> verts, Span<uint> idx, ref int ic, uint a, uint b, uint c) {
		bool cw = winding((Vector2)verts[(int)a], (Vector2)verts[(int)b], (Vector2)verts[(int)c]) > 0f;
		idx[ic++] = a;
		idx[ic++] = cw ? c : b;
		idx[ic++] = cw ? b : c;
	}

	/// <summary>
	/// Adds a segment's quad between the pos/neg vertices at its start and at its end, as two
	/// triangles, which are ccw since pos is the <see cref="perp"/> side of the segment's direction.
	/// </summary>
	private static void segmentQuad(Span<uint> idx, ref int ic, uint startPos, uint startNeg, uint endPos, uint endNeg) {
		idx[ic++] = startPos;
		idx[ic++] = endPos;
		idx[ic++] = startNeg;
		idx[ic++] = startNeg;
		idx[ic++] = endPos;
		idx[ic++] = endNeg;
	}

	bool IHasInvalidDefault.IsDefault => ownedCanvas is null;
}
