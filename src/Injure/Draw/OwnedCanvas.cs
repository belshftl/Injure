// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

// =================================================================================================
//
// A *draw range*, or just a *range*, is a run of consecutive draws that all share GPU state and can
// become one DrawIndexed, i.e. all share:
// - the pipeline (texture interpretation, blend state, write mask, target format)
// - the bound texture (only exactly one texture at a time is ever bound)
// - the geometry chunk (see CanvasArena.cs)
// - the uniform (ortho projection for the current target, and SDF parameters)
//
// Every draw goes through OwnedCanvas.reserve(), which compares that state with the current
// range's; if it doesn't match, the current range is recorded into the pass via closeRange() and a
// new one is started via startRange().
//
// Untextured draws use a 1x1 white texture and the Color texture interpretation.
//
// Using a 1x1 white texture rather than just not sampling a texture allows reusing the same shader
// (textured2dColor.wgsl) and vertex layout (Float32x2 pos, Float32x2 uv, Unorm8x4 color), which in
// turn allows reusing the whole pipeline, for untextured draws and textured draws with the Color
// texture interpretation, which are the two overwhelmingly common cases. The win is much larger
// than the miniscule extra overhead of sampling a 1x1 texture.
//
// In a similar vein, the transform matrix is applied on the CPU rather than a vertex shader to
// avoid having to split the range when a new transform matrix is pushed to the params stack. The
// exact same uniform binding can be reused when the transform matrix changes.
//
// The scissor/target are render pass state rather than range state, as transition() already handles
// them by closing the range (among other things).
//
// Ranges are recorded when they end, so a range's index count is known by then. Commands reference
// regions of the arena's GPU buffers whose data is only uploaded when the OwnedCanvas is disposed,
// which is sound because the uploads are queue writes (WriteToBuffer) and are ordered before the
// frame's command buffer is submitted.
//
// Additional optimizations/notes:
// - Consecutive ranges with the same uniform share the same slot in the arena; startRange()
//   compares the new range's uniform with the last uniform added to the arena, and if they're the
//   same, the new range just points at that same slot, as to not append another copy of the
//   identical 96-byte uniform (in practice occupying 256 bytes due to dynamic offset requirements).
// - Since a render pass keeps its state (pipeline, bind groups, vertex/index buffers) between
//   draws, recording a range only needs the calls to bind new state rather than all of it, so
//   closeRange() skips Set* calls for state the pass already has.
// - The baseVertex for the DrawIndexed drawing a range is always set to 0, with reserve() doing the
//   offsetting manually on the CPU instead, since a range covers many draws whose vertices start at
//   different positions while the baseVertex is a single value for the entire DrawIndexed.
//
// =================================================================================================

// =================================================================================================
//
// A note on "only exactly one texture at a time is ever bound":
//
// Currently, drawing a different texture splits a range, since we only bind one texture at a time.
// Since a 2D game world usually consists of mostly Color-interpreted textures and some untextured
// shapes (which are Color-interpreted 1x1 white textures too), and text/etc is usually on a higher
// HUD/UI layer, it brings up a question:
//
//   "Why batch if each new texture starts a new batch? Maybe bind multiple textures at once?"
//
// The problem right now is just that bind groups are immutable. As such, each distinct set of N
// textures needs its own bind group.
//
// If N = 1, as it currently is, each texture can just lazily create a bind group for itself and
// keep it for as long as the texture lives. That's what Texture2d already does.
//
// Increasing that to any useful batch count, though, such as 16, would entail creating a bind group
// every 16 draws and probably caching it somewhere, and that cache could hypothetically grow as
// large as all the possible permutations of 16 textures there are without ever getting a single
// cache hit; I don't think I have to explain that nPr(K, 16) where K can grow unbounded (and is
// often in the hundreds) is absurdly huge, and gets even worse if you increase the number from 16.
//
// It *could* still be useful if creating a bind group every 16 draws is somehow faster than 16
// SetBindGroup + DrawIndexed operations, or if the cache ends up being useful rather than an
// unbounded VRAM hog, maybe if games draw mostly the same things in the same order every frame or
// something like that.
//
// There are some potential optimizations here if you manage to somehow sort the textures so that
// draw order doesn't matter for cache hits, but that opens up a new can of worms with preserving
// the visible draw order, and doesn't solve the underlying problem.
//
// Bindless + non-uniform indexing seems to be partially supported by wgpu-native (the GLES backend
// doesn't support it, and it's uncertain whether/where Metal has non-uniform indexing) but doesn't
// fix the problem either, and is unportable across backends.
//
// So, in conclusion, it's not impossible but it's likely a pipe dream for now.
//
// =================================================================================================

/// <summary>
/// Wraps over a <see cref="RenderFrame"/> to provide a <see cref="Draw.Canvas"/> facade. Owns all
/// the state: said facade just keeps a reference to this object.
/// </summary>
/// <remarks>
/// <para>
/// Whatever owns the frame creates the <see cref="OwnedCanvas"/>, passes <see cref="Canvas"/> to be
/// propagated down through all the code that draws, nd disposes the <see cref="OwnedCanvas"/>
/// before submitting the frame. Disposing it ends the render pass and uploads what was drawn. The
/// frame is not submitted, and must be submitted manually.
/// </para>
/// <para>
/// Several canvases can draw into the same frame one after another, but not interleaved, since
/// their render passes can't overlap.
/// </para>
/// <para>
/// Canvases on the same device reuse the GPU memory they draw into across frames, and release it
/// once it went unused for a while; see
/// <see cref="SetMemoryPolicy(GpuDevice, CanvasMemoryPolicy)"/>.
/// </para>
/// <para>
/// See <see cref="Draw.Canvas"/> for more info regarding draws, parameters, etc.
/// </para>
/// <para>
/// Not thread-safe.
/// </para>
/// </remarks>
public sealed class OwnedCanvas : IDisposable {
	// ==========================================================================
	// state
	private readonly RenderFrame frame;
	private readonly CanvasDeviceResources res;
	private readonly CanvasArena arena;
	private readonly Stack<CanvasParams> @params = new();
	// one per override in @params, i.e. one fewer than @params; identifies the CanvasParamsScope
	// that pops it, so that disposing a scope twice (or a copy of one) can be told apart
	private readonly Stack<long> scopeIds = new();
	private long nextScopeId = 1;
	private RenderPass? pass;
	private bool disposed = false;

	// the draw range being accumulated; recorded into the pass once something about it changes
	private bool hasRange;
	private CanvasDeviceResources.PipelineKey rangeKey;
	private object? rangeTexture; // identity of the texture
	private GpuBindGroupRef? rangeTextureBindGroup;
	private CanvasArena.GeometryChunk? rangeChunk;
	private CanvasUniform rangeUniform;
	private CanvasArena.UniformChunk? rangeUniformChunk;
	private uint rangeUniformOffset;
	private uint rangeFirstIndex;
	private uint rangeIndexCount;

	// the most recently added uniform, reused while unchanged
	private bool hasLastUniform;
	private CanvasUniform lastUniform;
	private CanvasArena.UniformChunk? lastUniformChunk;
	private uint lastUniformOffset;

	// what's set on the current pass, to skip redundant calls
	private GpuRenderPipeline? setPipeline;
	private CanvasArena.UniformChunk? setUniformChunk;
	private uint setUniformOffset;
	private object? setTexture;
	private CanvasArena.GeometryChunk? setChunk;

	/// <summary>
	/// Creates a canvas drawing into <paramref name="frame"/>, and begins a render pass as dictated by
	/// <paramref name="baseParams"/>.
	/// </summary>
	/// <param name="frame">Frame to record into.</param>
	/// <param name="baseParams">The base of the parameters stack, which can't be popped off.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="frame"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="baseParams"/> is invalid: it targets a texture with an sRGB format,
	/// has a scissor of kind <see cref="CanvasScissorKind.Intersect"/> or with a negative size, or
	/// uses <see cref="TextureInterpretation.Sdf"/> without <see cref="CanvasMaterial.SdfParams"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="frame"/> has already been submitted or disposed, has a render pass
	/// active, or has no primary output while <paramref name="baseParams"/> targets it.
	/// </exception>
	public OwnedCanvas(RenderFrame frame, in CanvasParams baseParams) {
		ArgumentNullException.ThrowIfNull(frame);
		this.frame = frame;
		res = CanvasDeviceResources.For(frame.Device);
		validate(baseParams);
		arena = res.ArenaPool.Rent(frame);
		@params.Push(baseParams);
		openPass(in baseParams);
	}

	/// <summary>
	/// The canvas to draw with.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this canvas has been disposed.
	/// </exception>
	public Canvas Canvas {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return new Canvas(this);
		}
	}

	/// <summary>
	/// Sets how long canvases on <paramref name="device"/> keep GPU memory they don't need anymore,
	/// in case it turns out after a bit more time they actually need it again.
	/// </summary>
	/// <param name="device">Device the policy applies to.</param>
	/// <param name="policy">The policy; see <see cref="CanvasMemoryPolicy"/>.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if either part of <paramref name="policy"/> is negative.
	/// </exception>
	/// <remarks>
	/// Takes effect from the next time a canvas finishes. Devices start with
	/// <see cref="CanvasMemoryPolicy.Default"/>.
	/// </remarks>
	public static void SetMemoryPolicy(GpuDevice device, CanvasMemoryPolicy policy) {
		ArgumentNullException.ThrowIfNull(device);
		ArgumentOutOfRangeException.ThrowIfNegative(policy.RetainForCanvases, nameof(policy));
		ArgumentOutOfRangeException.ThrowIfLessThan(policy.RetainForTime, TimeSpan.Zero, nameof(policy));
		CanvasDeviceResources.For(device).ArenaPool.Policy = policy;
	}

	/// <summary>
	/// Gets how long canvases on <paramref name="device"/> keep GPU memory they don't need anymore.
	/// </summary>
	/// <param name="device">Device to get the policy of.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> is <see langword="null"/>.
	/// </exception>
	public static CanvasMemoryPolicy GetMemoryPolicy(GpuDevice device) {
		ArgumentNullException.ThrowIfNull(device);
		return CanvasDeviceResources.For(device).ArenaPool.Policy;
	}

	/// <summary>
	/// Records the remaining draws, ends the render pass, and uploads what was drawn. Doesn't submit
	/// the frame.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		closeRange();
		closePass();
		arena.Upload();
	}

	// ==========================================================================
	// used by Canvas
	internal CanvasParams CurrentParams {
		get {
			chk();
			return @params.Peek();
		}
	}

	internal uint TargetWidth {
		get {
			chk();
			CanvasTarget t = @params.Peek().Target;
			return t.IsPrimary ? frame.PrimaryView.Width : t.RenderTarget.Width;
		}
	}

	internal uint TargetHeight {
		get {
			chk();
			CanvasTarget t = @params.Peek().Target;
			return t.IsPrimary ? frame.PrimaryView.Height : t.RenderTarget.Height;
		}
	}

	internal CanvasParamsScope PushParams(scoped in CanvasParamsOverride ov) {
		chk();
		CanvasParams old = @params.Peek();
		CanvasParams @new = merge(in old, in ov);
		validate(@new);
		transition(in old, in @new, returning: false);
		@params.Push(@new);
		long id = nextScopeId++;
		scopeIds.Push(id);
		return new CanvasParamsScope(this, id);
	}

	/// <remarks>
	/// Vertices are in local coordinates, i.e. before the current transform; indices are relative to
	/// the first vertex. Same for the other <c>Add*</c> methods.
	/// </remarks>
	internal void AddPrimitive(ReadOnlySpan<Vertex2dColor> verts, ReadOnlySpan<uint> indices) {
		chk();
		Matrix3x2 m = @params.Peek().Transform;
		Span<Vertex2dTextureColor> dst = reserve(res.White, res.White.BindGroup, textured: false, verts.Length, indices);
		for (int i = 0; i < verts.Length; i++) {
			var p = Vector2.Transform(new Vector2(verts[i].X, verts[i].Y), m);
			dst[i] = new Vertex2dTextureColor(p.X, p.Y, 0f, 0f, verts[i].Color);
		}
	}

	internal void AddPrimitive(ReadOnlySpan<Vector2> points, SrgbColor32 color, ReadOnlySpan<uint> indices) {
		chk();
		Matrix3x2 m = @params.Peek().Transform;
		Span<Vertex2dTextureColor> dst = reserve(res.White, res.White.BindGroup, textured: false, points.Length, indices);
		for (int i = 0; i < points.Length; i++) {
			var p = Vector2.Transform(points[i], m);
			dst[i] = new Vertex2dTextureColor(p.X, p.Y, 0f, 0f, color);
		}
	}

	internal void AddTextured(in ResolvedTextureSource tex, ReadOnlySpan<Vertex2dTextureColor> verts, ReadOnlySpan<uint> indices) {
		chk();
		CanvasTarget target = @params.Peek().Target;
		if (!target.IsPrimary && tex.SameRenderTargetAs(target.RenderTarget))
			throw new InvalidOperationException("cannot draw a render target while it is the active canvas target (i.e. draw it into itself)");
		Matrix3x2 m = @params.Peek().Transform;
		Span<Vertex2dTextureColor> dst = reserve(tex.Identity, tex.BindGroup, textured: true, verts.Length, indices);
		for (int i = 0; i < verts.Length; i++) {
			var p = Vector2.Transform(new Vector2(verts[i].X, verts[i].Y), m);
			dst[i] = new Vertex2dTextureColor(p.X, p.Y, verts[i].U, verts[i].V, verts[i].Color);
		}
	}

	// ==========================================================================
	// draw ranges

	// starts a new range if this draw's state differs from the current one's, makes room for the
	// vertices, and appends the indices offset to them; returns the reserved vertices, which the
	// caller must fill completely
	private Span<Vertex2dTextureColor> reserve(object texture, GpuBindGroupRef textureBindGroup, bool textured, int vertexCount, ReadOnlySpan<uint> indices) {
		CanvasParams p = @params.Peek();
		TextureInterpretation interp = textured ? p.Material.TextureInterpretation : TextureInterpretation.Color;
		CanvasDeviceResources.PipelineKey key = new(interp, p.OutputState.Blend, p.OutputState.WriteMask, targetFormat(in p));
		CanvasUniform uniform = uniformFor(in p, interp);
		CanvasArena.GeometryChunk chunk = arena.ReserveGeometry(vertexCount, indices.Length);
		if (!hasRange || key != rangeKey || !ReferenceEquals(texture, rangeTexture) || chunk != rangeChunk || !uniform.Equals(rangeUniform)) {
			closeRange();
			startRange(in key, texture, textureBindGroup, chunk, in uniform);
		}
		int v0 = chunk.VertexCount;
		Span<uint> dstIndices = chunk.Indices.AsSpan(chunk.IndexCount, indices.Length);
		for (int i = 0; i < indices.Length; i++)
			dstIndices[i] = (uint)v0 + indices[i];
		chunk.VertexCount += vertexCount;
		chunk.IndexCount += indices.Length;
		rangeIndexCount += (uint)indices.Length;
		return chunk.Vertices.AsSpan(v0, vertexCount);
	}

	private void startRange(in CanvasDeviceResources.PipelineKey key, object texture, GpuBindGroupRef textureBindGroup, CanvasArena.GeometryChunk chunk, in CanvasUniform uniform) {
		if (!hasLastUniform || !uniform.Equals(lastUniform)) {
			(lastUniformChunk, lastUniformOffset) = arena.AddUniform(in uniform);
			lastUniform = uniform;
			hasLastUniform = true;
		}
		hasRange = true;
		rangeKey = key;
		rangeTexture = texture;
		rangeTextureBindGroup = textureBindGroup;
		rangeChunk = chunk;
		rangeUniform = uniform;
		rangeUniformChunk = lastUniformChunk;
		rangeUniformOffset = lastUniformOffset;
		rangeFirstIndex = (uint)chunk.IndexCount;
		rangeIndexCount = 0;
	}

	private void closeRange() {
		if (!hasRange)
			return;
		hasRange = false;
		if (rangeIndexCount == 0)
			return;
		RenderPass ps = pass ?? throw new InternalStateException("draw range without an active render pass");
		GpuRenderPipeline pipeline = res.GetPipeline(in rangeKey);
		if (!ReferenceEquals(pipeline, setPipeline)) {
			ps.SetPipeline(pipeline);
			setPipeline = pipeline;
		}
		if (rangeUniformChunk != setUniformChunk || rangeUniformOffset != setUniformOffset) {
			ps.SetBindGroup(0, rangeUniformChunk!.BindGroup, [rangeUniformOffset]);
			setUniformChunk = rangeUniformChunk;
			setUniformOffset = rangeUniformOffset;
		}
		if (!ReferenceEquals(rangeTexture, setTexture)) {
			ps.SetBindGroup(1, rangeTextureBindGroup!);
			setTexture = rangeTexture;
		}
		if (rangeChunk != setChunk) {
			ps.SetVertexBuffer(0, rangeChunk!.VertexBuffer);
			ps.SetIndexBuffer(rangeChunk.IndexBuffer, IndexFormat.Uint32);
			setChunk = rangeChunk;
		}
		ps.DrawIndexed(rangeIndexCount, firstIndex: rangeFirstIndex);
	}

	private CanvasUniform uniformFor(in CanvasParams p, TextureInterpretation interp) {
		(uint w, uint h) = targetSize(in p);
		CanvasUniform u = new() { Projection = MatrixUtil.OrthoTopLeft(w, h) };
		if (interp == TextureInterpretation.Sdf && p.Material.SdfParams is SdfParams sdf) {
			u.DistanceRangeTexels = sdf.DistanceRangeTexels;
			u.EdgeValue = sdf.EdgeValue;
			u.SoftnessPixels = sdf.SoftnessPixels;
			u.OutlineWidthPixels = sdf.OutlineWidthPixels;
			u.OutlineColor = sdf.OutlineColor.ToRawF128().ToVector4();
		}
		return u;
	}

	private (uint Width, uint Height) targetSize(in CanvasParams p) =>
		p.Target.IsPrimary ? (frame.PrimaryView.Width, frame.PrimaryView.Height) : (p.Target.RenderTarget.Width, p.Target.RenderTarget.Height);

	private TextureFormat targetFormat(in CanvasParams p) =>
		p.Target.IsPrimary ? frame.PrimaryView.Format : p.Target.RenderTarget.ColorFormat;

	// ==========================================================================
	// params

	// called from CanvasParamsScope.Dispose, which is public
	internal void PopParams(long scopeId) {
		// the whole stack is gone once this canvas is disposed, and an id that's no longer on the stack
		// belongs to a scope that was already disposed
		if (disposed || !scopeIds.Contains(scopeId))
			return;
		if (scopeIds.Peek() != scopeId)
			throw new InvalidOperationException("canvas parameter overrides must be disposed in reverse order of pushing");
		if (@params.Count <= 1)
			throw new InternalStateException("CanvasParamsScope tried to pop base canvas params off the stack");
		scopeIds.Pop();
		CanvasParams old = @params.Pop();
		transition(in old, @params.Peek(), returning: true);
	}

	private static CanvasParams merge(in CanvasParams curr, in CanvasParamsOverride ov) => new(
		Target: ov.Target ?? curr.Target,
		ColorAttachmentOps: ov.ColorAttachmentOps ?? curr.ColorAttachmentOps,
		Scissor: mergeScissor(curr.Scissor, ov.Scissor),
		Transform: ov.Transform ?? curr.Transform,
		OutputState: ov.OutputState ?? curr.OutputState,
		Material: ov.Material ?? curr.Material
	);

	// output state, material, and transform changes need no action here, as the next draw starts a
	// new range if its pipeline or uniform differs, and transforms are applied per vertex
	private void transition(in CanvasParams from, in CanvasParams to, bool returning) {
		if (pass is null)
			throw new InternalStateException("was expecting an open render pass to be there for params transition");
		if (from.Target != to.Target || from.ColorAttachmentOps != to.ColorAttachmentOps) {
			closeRange();
			closePass();
			// returning keeps what was drawn rather than e.g. clearing the target again
			openPass(returning ? to with { ColorAttachmentOps = ColorAttachmentOps.Load } : to);
		} else if (from.Scissor != to.Scissor) {
			closeRange();
			applyScissor(pass, in to);
		}
	}

	private void validate(in CanvasParams p) {
		TextureFormat fmt = targetFormat(in p);
		if (fmt.IsSrgb())
			throw new ArgumentException($"Canvas can't draw into a target with sRGB format {fmt}, since its colors are already sRGB-encoded");
		if (p.Scissor.Kind == CanvasScissorKind.Intersect)
			throw new ArgumentException("CanvasParams.Scissor cannot be Intersect as there is no existing scissor to intersect with");
		if (p.Scissor.Kind == CanvasScissorKind.Set && (p.Scissor.Rect.Width < 0 || p.Scissor.Rect.Height < 0))
			throw new ArgumentException("scissor rect cannot have negative width/height");
		if (p.Material.TextureInterpretation == TextureInterpretation.Sdf && p.Material.SdfParams is null)
			throw new ArgumentException("CanvasParams.Material.SdfParams must be set if the texture interpretation of the material is SDF");
	}

	// ==========================================================================
	// scissor
	private static RectI intersect(RectI a, RectI b) {
		int x1 = Math.Max(a.X, b.X);
		int y1 = Math.Max(a.Y, b.Y);
		int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
		int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
		x2 = Math.Max(x1, x2);
		y2 = Math.Max(y1, y2);
		return new RectI(x1, y1, x2 - x1, y2 - y1);
	}

	private static CanvasScissor mergeScissor(CanvasScissor curr, CanvasScissor? ov) {
		if (ov is not CanvasScissor s)
			return curr;
		if (curr.Kind == CanvasScissorKind.Intersect)
			throw new InternalStateException("Intersect scissor made it to mergeScissor");
		return s.Kind.Tag switch {
			CanvasScissorKind.Case.None => CanvasScissor.None,
			CanvasScissorKind.Case.Set => CanvasScissor.Set(s.Rect),
			CanvasScissorKind.Case.Intersect => curr.Kind.Tag switch {
				CanvasScissorKind.Case.None => CanvasScissor.Set(s.Rect),
				CanvasScissorKind.Case.Set => CanvasScissor.Set(intersect(curr.Rect, s.Rect)),
				_ => throw new UnreachableException(),
			},
			_ => throw new UnreachableException(),
		};
	}

	private void applyScissor(RenderPass ps, in CanvasParams p) {
		(uint w, uint h) = targetSize(in p);
		RectI full = new(0, 0, (int)w, (int)h);
		RectI effective = p.Scissor.Kind.Tag switch {
			CanvasScissorKind.Case.None => full,
			CanvasScissorKind.Case.Set => intersect(full, p.Scissor.Rect),
			CanvasScissorKind.Case.Intersect =>
				throw new InternalStateException("Intersect scissor made it to applyScissor"),
			_ => throw new UnreachableException(),
		};
		ps.SetScissorRect((uint)effective.X, (uint)effective.Y, (uint)effective.Width, (uint)effective.Height);
	}

	// ==========================================================================
	// passes
	private void openPass(in CanvasParams p) {
		if (pass is not null)
			throw new InternalStateException("tried to open a render pass but there's already an active one");
		pass = p.Target.IsPrimary
			? frame.BeginPrimaryPass(p.ColorAttachmentOps)
			: frame.Encoder.BeginColorPass(p.Target.RenderTarget.ColorView, p.ColorAttachmentOps);
		setPipeline = null;
		setUniformChunk = null;
		setTexture = null;
		setChunk = null;
		applyScissor(pass, in p);
	}

	private void closePass() {
		if (pass is null)
			throw new InternalStateException("tried to close the active render pass but there isn't one");
		pass.Dispose();
		pass = null;
	}

	private void chk() => ObjectDisposedException.ThrowIf(disposed, this);
}
