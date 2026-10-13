// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Parameters for <see cref="Canvas"/>.
/// </summary>
/// <param name="Target">What to draw into.</param>
/// <param name="ColorAttachmentOps">
/// What to do with <paramref name="Target"/>'s contents when its render pass begins, e.g. clear it.
/// </param>
/// <param name="Scissor">Rectangle that draws are clipped to.</param>
/// <param name="Transform">Transform matrix applied to the positions of draws.</param>
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
/// A scissor of kind <see cref="CanvasScissorKind.Intersect"/> is invalid in the base parameters,
/// since there's no scissor to intersect with.
/// </para>
/// <para>
/// Changing <paramref name="Target"/> or <paramref name="ColorAttachmentOps"/> ends the current
/// render pass and begins a new one. Returning to an earlier state by disposing an override keeps
/// the target's contents (the pass is begun with <see cref="LoadOp.Load"/>), rather than applying
/// that state's <paramref name="ColorAttachmentOps"/> again. Changing
/// <paramref name="OutputState"/>, <paramref name="Material"/>, <paramref name="Sampling"/>, or
/// <paramref name="Scissor"/> starts a new draw call; changing <paramref name="Transform"/>
/// doesn't, since it's applied to vertices on the CPU.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <paramref name="ColorAttachmentOps"/>
/// is.
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
) {
	/// <summary>
	/// Creates parameters for drawing into the primary target, clearing it to
	/// <paramref name="clearColor"/> first.
	/// </summary>
	/// <param name="clearColor">Color the target is cleared to.</param>
	/// <inheritdoc cref="Primary(ColorAttachmentOps)" path="/remarks"/>
	public static CanvasParams Primary(SrgbColor32 clearColor) =>
		Primary(ColorAttachmentOps.Clear(clearColor.ToRawF128()));

	/// <summary>
	/// Creates parameters for drawing into the primary target, with the given
	/// <see cref="ColorAttachmentOps"/>.
	/// </summary>
	/// <param name="colorAttachmentOps">
	/// What to do with the target's contents when its render pass begins, e.g.
	/// <see cref="ColorAttachmentOps.Load"/> to draw over them.
	/// </param>
	/// <remarks>
	/// The other parameters are: no scissor, the identity transform,
	/// <see cref="CanvasOutputStates.Alpha"/>, <see cref="CanvasMaterials.Color"/>, and
	/// <see cref="TexSampling.Nearest"/>. Use a <see langword="with"/> expression to change some of
	/// them.
	/// </remarks>
	public static CanvasParams Primary(ColorAttachmentOps colorAttachmentOps) => new(
		Target: CanvasTarget.Primary,
		ColorAttachmentOps: colorAttachmentOps,
		Scissor: CanvasScissor.None,
		Transform: Matrix3x2.Identity,
		OutputState: CanvasOutputStates.Alpha,
		Material: CanvasMaterials.Color,
		Sampling: TexSampling.Nearest
	);
}

/// <summary>
/// A partial override applied on top of the current <see cref="CanvasParams"/>.
/// </summary>
/// <param name="Target">Overrides <see cref="CanvasParams.Target"/>.</param>
/// <param name="ColorAttachmentOps">
/// Overrides <see cref="CanvasParams.ColorAttachmentOps"/>.
/// </param>
/// <param name="Scissor">
/// Overrides <see cref="CanvasParams.Scissor"/>; <see cref="CanvasScissorKind.Intersect"/>
/// intersects with the current scissor.
/// </param>
/// <param name="Transform">
/// Overrides <see cref="CanvasParams.Transform"/>; the override replaces the current transform
/// rather than being combined with it.
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
	CanvasScissor? Scissor = null,
	Matrix3x2? Transform = null,
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
	private readonly long id;

	internal CanvasParamsScope(OwnedCanvas canvas, long id) {
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
