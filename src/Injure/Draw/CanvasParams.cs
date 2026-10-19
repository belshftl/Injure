// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Parameters for <see cref="Canvas"/>.
/// </summary>
/// <param name="Target">
/// What to draw into; also carries the <see cref="Viewport"/> that defines the drawable area.
/// </param>
/// <param name="ColorAttachmentOps">
/// What to do with <paramref name="Target"/>'s contents when its render pass begins, e.g. clear it.
/// </param>
/// <param name="Scissor">
/// Rectangle of the drawable area that draws are clipped to, in addition to being clipped to the
/// drawable area itself.
/// </param>
/// <param name="Transform">
/// Transform matrix mapping draw coordinates to the drawable area, in drawable units. Must be
/// invertible (see <see cref="MatrixInvert"/>).
/// </param>
/// <param name="OutputState">
/// How draws are written to the target: blending and color write mask.
/// </param>
/// <param name="Material">How textured draws interpret their textures.</param>
/// <param name="Sampling">
/// How textured draws sample their textures, unless overridden by <see cref="TexDraw.Sampling"/>;
/// see <see cref="TexSampling"/> for when to use which.
/// </param>
/// <remarks>
/// <para>
/// Changing the render target of <paramref name="Target"/> (see
/// <see cref="CanvasTarget.SameRenderTarget(CanvasTarget)"/>) or
/// <paramref name="ColorAttachmentOps"/> ends the current render pass and begins a new one.
/// Returning to an earlier state by disposing an override keeps the target's contents (the pass is
/// begun with <see cref="LoadOp.Load"/>), rather than applying that state's
/// <paramref name="ColorAttachmentOps"/> again. Changing <paramref name="OutputState"/>,
/// <paramref name="Material"/>, <paramref name="Sampling"/>, or <paramref name="Scissor"/> starts a
/// new draw call, and so does changing only the viewport of <paramref name="Target"/> if that
/// changes the clip; changing <paramref name="Transform"/> doesn't, since it's applied to vertices
/// on the CPU.
/// </para>
/// <para>
/// The transform must be invertible, as <see cref="MatrixInvert"/> defines it. So must the
/// effective transform (see <see cref="EffectiveTransform"/>), which is capable of becoming
/// non-invertible at extreme scales even though the viewport always maps invertibly, since the
/// product can underflow or overflow.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <paramref name="Target"/>,
/// <paramref name="ColorAttachmentOps"/>, and <paramref name="Transform"/> are.
/// </para>
/// </remarks>
public readonly record struct CanvasParams(
	CanvasTarget Target,
	ColorAttachmentOps ColorAttachmentOps,
	CanvasScissor Scissor,
	Matrix3x2 Transform,
	CanvasOutputState OutputState,
	CanvasMaterial Material,
	TexSampling Sampling
) : IHasInvalidDefault {
	/// <summary>
	/// Creates parameters for drawing into the primary target with the given viewport, clearing it to
	/// <paramref name="clearColor"/> first.
	/// </summary>
	/// <param name="viewport">
	/// Drawable area of the primary target; see <see cref="CanvasTarget.Viewport"/>.
	/// </param>
	/// <param name="clearColor">Color the target is cleared to.</param>
	/// <inheritdoc cref="Primary(Viewport, ColorAttachmentOps)" path="/exception"/>
	/// <inheritdoc cref="Primary(Viewport, ColorAttachmentOps)" path="/remarks"/>
	public static CanvasParams Primary(Viewport viewport, SrgbColor32 clearColor) =>
		Primary(viewport, ColorAttachmentOps.Clear(clearColor.ToRawF128()));

	/// <summary>
	/// Creates parameters for drawing into the primary target with the given viewport and
	/// <see cref="ColorAttachmentOps"/>.
	/// </summary>
	/// <param name="viewport">
	/// Drawable area of the primary target; see <see cref="CanvasTarget.Viewport"/>.
	/// </param>
	/// <param name="colorAttachmentOps">
	/// What to do with the target's contents when its render pass begins, e.g.
	/// <see cref="ColorAttachmentOps.Load"/> to draw over them.
	/// </param>
	/// <remarks>
	/// The other parameters are: no scissor, the identity transform,
	/// <see cref="CanvasOutputStates.Alpha"/>, <see cref="CanvasMaterial.Color"/>, and
	/// <see cref="TexSampling.Nearest"/>. A <see langword="with"/> expression can be used to change
	/// some of them.
	/// </remarks>
	public static CanvasParams Primary(Viewport viewport, ColorAttachmentOps colorAttachmentOps) {
		ArgumentInvalidDefaultException.ThrowIfDefault(colorAttachmentOps);
		return new(
			Target: CanvasTarget.Primary(viewport),
			ColorAttachmentOps: colorAttachmentOps,
			Scissor: CanvasScissor.None,
			Transform: Matrix3x2.Identity,
			OutputState: CanvasOutputStates.Alpha,
			Material: CanvasMaterial.Color,
			Sampling: TexSampling.Nearest
		);
	}

	/// <summary>
	/// Creates parameters for drawing into all of <paramref name="frame"/>'s primary output at
	/// <paramref name="pixelsPerUnit"/>, clearing it to <paramref name="clearColor"/> first.
	/// </summary>
	/// <param name="frame">Frame whose primary output to draw into.</param>
	/// <param name="clearColor">Color the target is cleared to.</param>
	/// <param name="pixelsPerUnit">
	/// The side length of one drawable unit, in target pixels, e.g. 1 for physical pixels, or the
	/// window's pixel density for logical units.
	/// </param>
	/// <inheritdoc cref="Primary(RenderFrame, ColorAttachmentOps, float)" path="/exception"/>
	/// <inheritdoc cref="Primary(RenderFrame, ColorAttachmentOps, float)" path="/remarks"/>
	public static CanvasParams Primary(
		RenderFrame frame,
		SrgbColor32 clearColor,
		float pixelsPerUnit = 1f
	) => Primary(frame, ColorAttachmentOps.Clear(clearColor.ToRawF128()), pixelsPerUnit);

	/// <summary>
	/// Creates parameters for drawing into all of <paramref name="frame"/>'s primary output at
	/// <paramref name="pixelsPerUnit"/>, with the given <see cref="ColorAttachmentOps"/>.
	/// </summary>
	/// <param name="frame">Frame whose primary output to draw into.</param>
	/// <param name="colorAttachmentOps">
	/// What to do with the target's contents when its render pass begins, e.g.
	/// <see cref="ColorAttachmentOps.Load"/> to draw over them.
	/// </param>
	/// <param name="pixelsPerUnit">
	/// The side length of one drawable unit, in target pixels, e.g. 1 for physical pixels, or the
	/// window's pixel density for logical units.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="frame"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="pixelsPerUnit"/> isn't positive and finite.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="pixelsPerUnit"/> is too small for the viewport to map to pixels
	/// invertibly; see <see cref="Viewport.Whole(Vector2, float)"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="frame"/> has no primary output, or has already been submitted or
	/// disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The viewport is <see cref="Viewport.Whole(Vector2, float)"/> of the primary output's size, read
	/// from <paramref name="frame"/> now, so the parameters belong to this frame.
	/// </para>
	/// <para>
	/// The other parameters are as for <see cref="Primary(Viewport, ColorAttachmentOps)"/>.
	/// </para>
	/// </remarks>
	public static CanvasParams Primary(
		RenderFrame frame,
		ColorAttachmentOps colorAttachmentOps,
		float pixelsPerUnit = 1f
	) {
		ArgumentNullException.ThrowIfNull(frame);
		ArgumentInvalidDefaultException.ThrowIfDefault(colorAttachmentOps);
		GpuTextureViewRef view = frame.PrimaryView;
		return Primary(Viewport.Whole(new Vector2(view.Width, view.Height), pixelsPerUnit), colorAttachmentOps);
	}

	/// <summary>
	/// <c>Transform * Target.Viewport.ToPixelsMatrix</c>. Maps draw coordinates to target pixels.
	/// </summary>
	public Matrix3x2 EffectiveTransform => Transform * Target.Viewport.ToPixelsMatrix;

	/// <summary>
	/// Returns these parameters with <paramref name="ov"/> applied, as
	/// <see cref="Canvas.PushParams(in CanvasParamsOverride)"/> applies it.
	/// </summary>
	/// <param name="ov">Override to apply.</param>
	/// <returns>
	/// The parameters <see cref="Canvas.PushParams(in CanvasParamsOverride)"/> would push.
	/// </returns>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="ov"/>'s transform or scissor override is the
	/// <see langword="default"/> value.
	/// </exception>
	/// <remarks>
	/// <para>
	/// A <see langword="null"/> field of <paramref name="ov"/> keeps the current value, and a
	/// non-<see langword="null"/> one replaces it, except for the transform and scissor, whose
	/// overrides can also combine with the current value; see <see cref="CanvasTransformOverride"/>
	/// and <see cref="CanvasScissorOverride"/>.
	/// </para>
	/// <para>
	/// This doesn't validate the result. <see cref="Canvas.PushParams(in CanvasParamsOverride)"/>
	/// does, and can reject it, e.g. if a <see cref="CanvasTransformOverrideKind.Multiply"/> override
	/// made the transform non-invertible, or the new target has an sRGB format.
	/// </para>
	/// </remarks>
	public CanvasParams Merge(in CanvasParamsOverride ov) => new(
		Target: ov.Target ?? Target,
		ColorAttachmentOps: ov.ColorAttachmentOps ?? ColorAttachmentOps,
		Scissor: mergeScissor(Scissor, ov.Scissor),
		Transform: mergeTransform(Transform, ov.Transform),
		OutputState: ov.OutputState ?? OutputState,
		Material: ov.Material ?? Material,
		Sampling: ov.Sampling ?? Sampling
	);

	private static Matrix3x2 mergeTransform(Matrix3x2 curr, CanvasTransformOverride? ov) {
		if (ov is not CanvasTransformOverride t)
			return curr;
		if (t.IsDefault)
			throw ArgumentInvalidDefaultException.For<CanvasTransformOverride>(nameof(ov));
		return t.Kind.Tag switch {
			CanvasTransformOverrideKind.Case.Set => t.Matrix,
			// row vectors: the override applies first, then the current transform
			CanvasTransformOverrideKind.Case.Multiply => t.Matrix * curr,
			_ => throw new UnreachableException(),
		};
	}

	private static CanvasScissor mergeScissor(CanvasScissor curr, CanvasScissorOverride? ov) {
		if (ov is not CanvasScissorOverride s)
			return curr;
		if (s.IsDefault)
			throw ArgumentInvalidDefaultException.For<CanvasScissorOverride>(nameof(ov));
		return s.Kind.Tag switch {
			CanvasScissorOverrideKind.Case.NoScissor => CanvasScissor.None,
			CanvasScissorOverrideKind.Case.Set => CanvasScissor.Set(s.Rect),
			CanvasScissorOverrideKind.Case.Intersect => curr.Kind.Tag switch {
				CanvasScissorKind.Case.None => CanvasScissor.Set(s.Rect),
				CanvasScissorKind.Case.Set => CanvasScissor.Set(intersect(curr.Rect, s.Rect)),
				_ => throw new UnreachableException(),
			},
			_ => throw new UnreachableException(),
		};
	}

	// no overlap = RectF.Empty, so all empty intersections compare equal
	private static RectF intersect(RectF a, RectF b) {
		_ = a.TryIntersect(b, out RectF result);
		return result;
	}

	// prevent the generated ToString from printing EffectiveTransform, which throws on default
	public override string ToString() =>
		$"CanvasParams {{ Target = {Target}, ColorAttachmentOps = {ColorAttachmentOps}, Scissor = {Scissor}, Transform = {Transform}, OutputState = {OutputState}, Material = {Material}, Sampling = {Sampling} }}";

	bool IHasInvalidDefault.IsDefault => Target.IsDefault;
}

/// <summary>
/// A partial override applied on top of the current <see cref="CanvasParams"/>.
/// </summary>
/// <param name="Target">Overrides <see cref="CanvasParams.Target"/>.</param>
/// <param name="ColorAttachmentOps">
/// Overrides <see cref="CanvasParams.ColorAttachmentOps"/>.
/// </param>
/// <param name="Scissor">
/// Overrides <see cref="CanvasParams.Scissor"/>, either replacing or intersecting with the current
/// scissor; see <see cref="CanvasScissorOverride"/>.
/// </param>
/// <param name="Transform">
/// Overrides <see cref="CanvasParams.Transform"/>, either replacing the current transform or
/// multiplying into it; see <see cref="CanvasTransformOverride"/>.
/// </param>
/// <param name="OutputState">Overrides <see cref="CanvasParams.OutputState"/>.</param>
/// <param name="Material">Overrides <see cref="CanvasParams.Material"/>.</param>
/// <param name="Sampling">Overrides <see cref="CanvasParams.Sampling"/>.</param>
/// <remarks>
/// <para>
/// A <see langword="null"/> field keeps the current value.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and doesn't override anything.
/// </para>
/// </remarks>
public readonly record struct CanvasParamsOverride(
	CanvasTarget? Target = null,
	ColorAttachmentOps? ColorAttachmentOps = null,
	CanvasScissorOverride? Scissor = null,
	CanvasTransformOverride? Transform = null,
	CanvasOutputState? OutputState = null,
	CanvasMaterial? Material = null,
	TexSampling? Sampling = null
);

/// <summary>
/// Restores the parameters from before a <see cref="Canvas.PushParams(in CanvasParamsOverride)"/>
/// call when disposed.
/// </summary>
/// <remarks>
/// <para>
/// Intended for <see langword="using"/> statements. Disposing pops the override if it's on top of
/// the stack, and does nothing if it was already popped, e.g. when a scope (or a copy of one) is
/// disposed again, or once the <see cref="OwnedCanvas"/> has been disposed.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and does nothing when disposed.
/// </para>
/// </remarks>
public readonly ref struct CanvasParamsScope {
	private readonly OwnedCanvas? canvas;
	private readonly ulong id;

	internal CanvasParamsScope(OwnedCanvas canvas, ulong id) {
		this.canvas = canvas;
		this.id = id;
	}

	/// <summary>
	/// Pops the override this scope was returned for, restoring the previous parameters.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if an override pushed after this one is still on the stack.
	/// </exception>
	public void Dispose() => canvas?.PopParams(id);
}
