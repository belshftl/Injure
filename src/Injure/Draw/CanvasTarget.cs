// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// Target for a <see cref="Canvas"/> to draw into (either the frame's primary output or an
/// offscreen <see cref="RenderTarget2d"/>), plus the <see cref="Draw.Viewport"/> that defines its
/// drawable area and the <see cref="CanvasLoadOp"/> to use for it.
/// </summary>
/// <remarks>
/// <para>
/// Drawn geometry is transformed by <see cref="CanvasParams.Transform"/>, then placed on the
/// drawable area, and <see cref="Viewport"/> maps the drawable area to the target's pixels. The viewport belongs to
/// the target rather than to the transform, so
/// <see cref="CanvasTransformOverride.Set(Matrix3x2)"/> only replaces the transform, and the
/// viewport remains unchanged. Every draw is clipped to the drawable area. An example viewport for
/// the primary output would cover the whole target at the window's pixel density, making it so
/// that drawable units correspond to logical units rather than physical pixels.
/// </para>
/// <para>
/// Equality compares the render target, the viewport, and the load op; see
/// <see cref="SameRenderTarget(CanvasTarget)"/> if you only care about the render target.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasTarget : IEquatable<CanvasTarget>, IHasInvalidDefault {
	private readonly RenderTarget2d? rt;
	private readonly bool isPrimary;
	private readonly Viewport viewport;
	private readonly CanvasLoadOp loadOp;

	/// <summary>
	/// The target's drawable area.
	/// </summary>
	public Viewport Viewport => IsDefault ? throw InvalidDefaultException.For<CanvasTarget>() : viewport;

	/// <summary>
	/// What happens to the existing contents when the target is made the new active target in
	/// <see cref="Canvas"/>, but not when switching back to it after an override; see
	/// <see cerf="CanvasLoadOp"/>.
	/// </summary>
	public CanvasLoadOp LoadOp => IsDefault ? throw InvalidDefaultException.For<CanvasTarget>() : loadOp;

	/// <summary>
	/// Whether this is the frame's primary output; see <see cref="RenderFrame.PrimaryView"/>.
	/// </summary>
	[MemberNotNullWhen(false, nameof(rt))]
	public bool IsPrimary {
		get {
			if (IsDefault)
				throw InvalidDefaultException.For<CanvasTarget>();
			return isPrimary;
		}
	}

	/// <summary>
	/// The render target this refers to.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this is the primary output.
	/// </exception>
	public RenderTarget2d RenderTarget => IsPrimary
		? throw new InvalidOperationException("this is the primary target, which has no RenderTarget2d")
		: rt;

	private CanvasTarget(RenderTarget2d? rt, bool isPrimary, Viewport viewport, CanvasLoadOp loadOp) {
		this.rt = rt;
		this.isPrimary = isPrimary;
		this.viewport = viewport;
		this.loadOp = loadOp;
	}

	/// <summary>
	/// Creates a target referring to the frame's primary output; see
	/// <see cref="RenderFrame.PrimaryView"/>.
	/// </summary>
	/// <param name="viewport">The drawable area of the primary output.</param>
	/// <param name="loadOp">
	/// What happens to the existing contents when a canvas begins drawing into the target.
	/// </param>
	public static CanvasTarget Primary(Viewport viewport, CanvasLoadOp loadOp) {
		ArgumentInvalidDefaultException.ThrowIfDefault(viewport);
		ArgumentInvalidDefaultException.ThrowIfDefault(loadOp);
		return new CanvasTarget(null, true, viewport, loadOp);
	}

	/// <summary>
	/// Creates a target referring to an offscreen render target, with a viewport covering all of it
	/// at 1 pixel per drawable unit.
	/// </summary>
	/// <param name="renderTarget">Render target to draw into.</param>
	/// <param name="loadOp">
	/// What happens to the existing contents when a canvas begins drawing into the target.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="renderTarget"/> is <see langword="null"/>.
	/// </exception>
	public static CanvasTarget Offscreen(RenderTarget2d renderTarget, CanvasLoadOp loadOp) {
		ArgumentNullException.ThrowIfNull(renderTarget);
		ArgumentInvalidDefaultException.ThrowIfDefault(loadOp);
		return new CanvasTarget(renderTarget, false, Viewport.Whole(new Vector2(renderTarget.Width, renderTarget.Height)), loadOp);
	}

	/// <summary>
	/// Creates a target referring to an offscreen render target.
	/// </summary>
	/// <param name="renderTarget">Render target to draw into.</param>
	/// <param name="loadOp">
	/// What happens to the existing contents when a canvas begins drawing into the target.
	/// </param>
	/// <param name="viewport">The drawable area of the render target.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="renderTarget"/> is <see langword="null"/>.
	/// </exception>
	public static CanvasTarget Offscreen(RenderTarget2d renderTarget, CanvasLoadOp loadOp, Viewport viewport) {
		ArgumentNullException.ThrowIfNull(renderTarget);
		ArgumentInvalidDefaultException.ThrowIfDefault(loadOp);
		ArgumentInvalidDefaultException.ThrowIfDefault(viewport);
		return new CanvasTarget(renderTarget, false, viewport, loadOp);
	}

	/// <summary>
	/// Whether this and <paramref name="other"/> refer to the same render target, or both to the
	/// primary output, ignoring the <see cref="Viewport"/> / <see cref="LoadOp"/> on both.
	/// </summary>
	public bool SameRenderTarget(CanvasTarget other) {
		ArgumentInvalidDefaultException.ThrowIfDefault(other);
		return IsPrimary == other.IsPrimary && ReferenceEquals(rt, other.rt);
	}

	public bool Equals(CanvasTarget other) =>
		isPrimary == other.isPrimary && ReferenceEquals(rt, other.rt) && viewport.Equals(other.viewport) && loadOp == other.loadOp;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasTarget other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(rt is null ? 0 : RuntimeHelpers.GetHashCode(rt), isPrimary, viewport, loadOp);
	public static bool operator ==(CanvasTarget left, CanvasTarget right) => left.Equals(right);
	public static bool operator !=(CanvasTarget left, CanvasTarget right) => !left.Equals(right);

	internal bool IsDefault => rt is null && !isPrimary;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
