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
	private readonly Stack<ulong> scopeIds = new();
	private ulong nextScopeId = 1;
	private RenderPass? pass;
	private bool disposed = false;

	// for tests checking what batches together
	internal int DrawCallCount { get; private set; }

	// the draw range being accumulated; recorded into the pass once something about it changes
	private bool hasRange;
	private CanvasDeviceResources.PipelineKey rangeKey;
	private object? rangeTexture; // identity of the texture
	private GpuBindGroupRef? rangeTextureBindGroup;
	private TexSampling rangeSampling;
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

	// scratch buffers for generated geometry
	private Vector2[] scratchPoints = new Vector2[128];
	private Vertex2dColor[] scratchVerts = new Vertex2dColor[128];
	private uint[] scratchIndices = new uint[256];
	private int[] scratchInts = new int[16];

	// what's set on the current pass, to skip redundant calls
	private GpuRenderPipeline? setPipeline;
	private CanvasArena.UniformChunk? setUniformChunk;
	private uint setUniformOffset;
	private object? setTexture;
	private TexSampling? setSampling;
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
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="baseParams"/>'s target is the <see langword="default"/> value.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="baseParams"/> is invalid: its target is a texture with an sRGB
	/// format, its transform or effective transform isn't invertible (see <see cref="MatrixInvert"/>),
	/// or it uses <see cref="TextureInterpretation.Sdf"/> without
	/// <see cref="CanvasMaterial.SdfParams"/>.
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

	internal Matrix3x2 EffectiveTransform {
		get {
			chk();
			return @params.Peek().EffectiveTransform;
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
		CanvasParams @new = old.Merge(in ov);
		validate(@new);
		transition(in old, in @new, returning: false);
		@params.Push(@new);
		ulong id = nextScopeId++;
		scopeIds.Push(id);
		return new CanvasParamsScope(this, id);
	}

	/// <remarks>
	/// Invalidates the value returned by the previous <see cref="ScratchPoints(int)"/> call, if any.
	/// </remarks>
	internal Span<Vector2> ScratchPoints(int count) => scratch(ref scratchPoints, count);

	/// <remarks>
	/// Invalidates the value returned by the previous <see cref="ScratchVerts(int)"/> call, if any.
	/// </remarks>
	internal Span<Vertex2dColor> ScratchVerts(int count) => scratch(ref scratchVerts, count);

	/// <remarks>
	/// Invalidates the value returned by the previous <see cref="ScratchIndices(int)"/> call, if any.
	/// </remarks>
	internal Span<uint> ScratchIndices(int count) => scratch(ref scratchIndices, count);

	/// <remarks>
	/// Invalidates the value returned by the previous <see cref="ScratchInts(int)"/> call, if any.
	/// </remarks>
	internal Span<int> ScratchInts(int count) => scratch(ref scratchInts, count);

	private static Span<T> scratch<T>(ref T[] arr, int count) where T : unmanaged {
		if (arr.Length < count)
			arr = new T[Math.Max(count, arr.Length * 2)];
		return arr.AsSpan(0, count);
	}

	/// <remarks>
	/// Vertices are in local coordinates, i.e. before the current transform; indices are relative to
	/// the first vertex. Same for the other <c>Add*</c> methods.
	/// </remarks>
	internal void AddPrimitive(ReadOnlySpan<Vertex2dColor> verts, ReadOnlySpan<uint> indices) {
		Matrix3x2 m = EffectiveTransform;
		Span<Vertex2dTextureColor> dst = reserve(res.White, res.White.BindGroup, untexturedSampling(), textured: false, verts.Length, indices, replace: false);
		for (int i = 0; i < verts.Length; i++) {
			var p = Vector2.Transform(new Vector2(verts[i].X, verts[i].Y), m);
			dst[i] = new Vertex2dTextureColor(p.X, p.Y, 0f, 0f, verts[i].Color);
		}
	}

	internal void AddPrimitive(ReadOnlySpan<Vector2> points, SrgbColor32 color, ReadOnlySpan<uint> indices) {
		Matrix3x2 m = EffectiveTransform;
		Span<Vertex2dTextureColor> dst = reserve(res.White, res.White.BindGroup, untexturedSampling(), textured: false, points.Length, indices, replace: false);
		for (int i = 0; i < points.Length; i++) {
			var p = Vector2.Transform(points[i], m);
			dst[i] = new Vertex2dTextureColor(p.X, p.Y, 0f, 0f, color);
		}
	}

	internal void AddTextured(in ResolvedDrawableTexture tex, TexSampling sampling, ReadOnlySpan<Vertex2dTextureColor> verts, ReadOnlySpan<uint> indices) {
		chk();
		CanvasTarget target = @params.Peek().Target;
		if (!target.IsPrimary && tex.SameRenderTargetAs(target.RenderTarget))
			throw new InvalidOperationException("cannot draw a render target while it is the active canvas target (i.e. draw it into itself)");
		Matrix3x2 m = EffectiveTransform;
		Span<Vertex2dTextureColor> dst = reserve(tex.Identity, tex.BindGroup, sampling, textured: true, verts.Length, indices, replace: false);
		for (int i = 0; i < verts.Length; i++) {
			var p = Vector2.Transform(new Vector2(verts[i].X, verts[i].Y), m);
			dst[i] = new Vertex2dTextureColor(p.X, p.Y, verts[i].U, verts[i].V, verts[i].Color);
		}
	}

	/// <summary>
	/// Draws a whole-target-sized quad ignoring the transform, output state, and write mask.
	/// <see cref="CanvasOutputStates.Opaque"/> is used. The scissor still applies.
	/// </summary>
	internal void Clear(SrgbColor32 color) {
		chk();
		(uint w, uint h) = targetSize(@params.Peek());
		Span<Vertex2dTextureColor> dst = reserve(res.White, res.White.BindGroup, untexturedSampling(), textured: false, 4, [0, 2, 1, 3, 1, 2], replace: true);
		dst[0] = new Vertex2dTextureColor(0f, 0f, 0f, 0f, color);
		dst[1] = new Vertex2dTextureColor(w, 0f, 0f, 0f, color);
		dst[2] = new Vertex2dTextureColor(0f, h, 0f, 0f, color);
		dst[3] = new Vertex2dTextureColor(w, h, 0f, 0f, color);
	}

	// ==========================================================================
	// draw ranges

	// the white texture looks the same under any sampler, so untextured draws keep the open range's
	// sampling rather than starting a new range over it
	private TexSampling untexturedSampling() => hasRange ? rangeSampling : @params.Peek().Sampling;

	// starts a new range if this draw's state differs from the current one's, makes room for the
	// vertices, and appends the indices offset to them; returns the reserved vertices, which the
	// caller must fill completely
	// `replace` toggles writing with CanvasOutputStates.Opaque instead of the current output state
	private Span<Vertex2dTextureColor> reserve(
		object texture,
		GpuBindGroupRef textureBindGroup,
		TexSampling sampling,
		bool textured,
		int vertexCount,
		ReadOnlySpan<uint> indices,
		bool replace
	) {
		CanvasParams p = @params.Peek();
		TextureInterpretation interp = textured ? p.Material.TextureInterpretation : TextureInterpretation.Color;
		CanvasOutputState output = replace ? CanvasOutputStates.Opaque : p.OutputState;
		CanvasDeviceResources.PipelineKey key = new(interp, output.Blend, output.WriteMask, targetFormat(in p));
		CanvasUniform uniform = uniformFor(in p, interp);
		CanvasArena.GeometryChunk chunk = arena.ReserveGeometry(vertexCount, indices.Length);
		if (!hasRange || key != rangeKey || !ReferenceEquals(texture, rangeTexture) || sampling != rangeSampling || chunk != rangeChunk || !uniform.Equals(rangeUniform)) {
			closeRange();
			startRange(in key, texture, textureBindGroup, sampling, chunk, in uniform);
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

	private void startRange(in CanvasDeviceResources.PipelineKey key, object texture, GpuBindGroupRef textureBindGroup, TexSampling sampling, CanvasArena.GeometryChunk chunk, in CanvasUniform uniform) {
		if (!hasLastUniform || !uniform.Equals(lastUniform)) {
			(lastUniformChunk, lastUniformOffset) = arena.AddUniform(in uniform);
			lastUniform = uniform;
			hasLastUniform = true;
		}
		hasRange = true;
		rangeKey = key;
		rangeTexture = texture;
		rangeTextureBindGroup = textureBindGroup;
		rangeSampling = sampling;
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
		if (rangeSampling != setSampling) {
			ps.SetBindGroup(2, res.GetSamplerBindGroup(rangeSampling));
			setSampling = rangeSampling;
		}
		if (rangeChunk != setChunk) {
			ps.SetVertexBuffer(0, rangeChunk!.VertexBuffer);
			ps.SetIndexBuffer(rangeChunk.IndexBuffer, IndexFormat.Uint32);
			setChunk = rangeChunk;
		}
		ps.DrawIndexed(rangeIndexCount, firstIndex: rangeFirstIndex);
		DrawCallCount++;
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
	internal void PopParams(ulong scopeId) {
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

	// output state, material, sampling, and transform changes need no action here, as the next draw starts a
	// new range if its pipeline or uniform differs, and transforms are applied per vertex
	private void transition(in CanvasParams from, in CanvasParams to, bool returning) {
		if (pass is null)
			throw new InternalStateException("was expecting an open render pass to be there for params transition");
		if (!from.Target.SameRenderTarget(to.Target) || from.ColorAttachmentOps != to.ColorAttachmentOps) {
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
		if (p.Target.IsDefault)
			throw ArgumentInvalidDefaultException.For<CanvasTarget>();
		// the view matrix is checked by the CanvasTarget factories
		if (!MatrixInvert.IsInvertible(p.Transform))
			throw new ArgumentException($"CanvasParams.Transform must be invertible, got {p.Transform}");
		if (!MatrixInvert.IsInvertible(p.EffectiveTransform))
			throw new ArgumentException($"the effective transform (CanvasParams.Transform * CanvasTarget.View) must be invertible, got {p.EffectiveTransform}");
		TextureFormat fmt = targetFormat(in p);
		if (fmt.IsSrgb())
			throw new ArgumentException($"Canvas can't draw into a target with sRGB format {fmt}, since its colors are already sRGB-encoded");
		if (p.Material.TextureInterpretation == TextureInterpretation.Sdf && p.Material.SdfParams is null)
			throw new ArgumentException("CanvasParams.Material.SdfParams must be set if the texture interpretation of the material is SDF");
	}

	// ==========================================================================
	// scissor
	private void applyScissor(RenderPass ps, in CanvasParams p) {
		(uint w, uint h) = targetSize(in p);
		RectI full = new(0, 0, (int)w, (int)h);
		RectI effective;
		switch (p.Scissor.Kind.Tag) {
		case CanvasScissorKind.Case.None: effective = full; break;
		// no overlap gives RectI.Empty, which clips everything
		case CanvasScissorKind.Case.Set: full.TryIntersect(p.Scissor.Rect, out effective); break;
		default: throw new UnreachableException();
		}
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
		setSampling = null;
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
