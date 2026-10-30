// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// What to do with the existing contents of a render target when it's made the new active target
/// in <see cref="Canvas"/>, but not when switching back to it after an override.
/// </summary>
/// <remarks>
/// <para>
/// Switching back to a previous active target by disposing a <see cref="CanvasParamsScope"/>
/// always acts like <see cref="Load"/>. This only applies when the canvas first begins drawing
/// into that target, i.e. either at <see cref="OwnedCanvas"/> creation or when a
/// <see cref="CanvasParamsOverride"/> that sets a new target is pushed.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasLoadOp : IEquatable<CanvasLoadOp>, IHasInvalidDefault {
	private enum Kind : byte {
		Invalid,
		Load,
		Clear,
	}

	private readonly Kind kind;
	private readonly SrgbStraight32 clearColor;

	private CanvasLoadOp(Kind kind, SrgbStraight32 clearColor) {
		this.kind = kind;
		this.clearColor = clearColor;
	}

	/// <summary>
	/// Keeps the target's existing contents.
	/// </summary>
	public static readonly CanvasLoadOp Load = new(Kind.Load, default);

	/// <summary>
	/// Clears the whole target to <paramref name="color"/>.
	/// </summary>
	/// <param name="color">Color to clear to.</param>
	/// <remarks>
	/// Keep in mind that this clears <i>the whole target</i>, regardless of
	/// <see cref="CanvasTarget.Viewport"/> and the scissor. As such, using this for the primary
	/// target with a letterboxing viewport also clears the area where the letterbox bars are to that
	/// color. If you want to clear to some non-black color but keep the letterbox bars black, clear
	/// to black here and then separately do <see cref="Canvas.ClearWithinScissor(SrgbStraight32)"/>
	/// with your non-black color.
	/// </remarks>
	public static CanvasLoadOp Clear(SrgbStraight32 color) => new(Kind.Clear, color);

	/// <summary>
	/// Whether this clears the target, i.e. was created with <see cref="Clear(SrgbStraight32)"/>.
	/// </summary>
	public bool IsClear => IsDefault ? throw InvalidDefaultException.For<CanvasLoadOp>() : kind == Kind.Clear;

	/// <summary>
	/// The color this clears the target to.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this is <see cref="Load"/>, i.e. if <see cref="IsClear"/> is false.
	/// </exception>
	public SrgbStraight32 ClearColor => IsClear
		? clearColor
		: throw new InvalidOperationException("CanvasLoadOp.Load has no clear color");

	internal ColorAttachmentOps ToColorAttachmentOps() => IsClear
		? ColorAttachmentOps.Clear(clearColor.ToRawF128())
		: ColorAttachmentOps.Load;

	public bool Equals(CanvasLoadOp other) => kind == other.kind && clearColor == other.clearColor;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasLoadOp other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(kind, clearColor);
	public static bool operator ==(CanvasLoadOp left, CanvasLoadOp right) => left.Equals(right);
	public static bool operator !=(CanvasLoadOp left, CanvasLoadOp right) => !left.Equals(right);

	public override string ToString() => kind switch {
		Kind.Load => "Load",
		Kind.Clear => $"Clear({clearColor})",
		_ => "<invalid>",
	};

	internal bool IsDefault => kind == Kind.Invalid;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
