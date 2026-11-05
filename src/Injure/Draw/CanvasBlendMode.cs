// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// How new draws on <see cref="Canvas"/> blend with the existing contents of the target.
/// </summary>
/// <remarks>
/// <para>
/// Blending happens in linear light on premultiplied colors. Throughout this type's members' docs,
/// <c>src</c> and <c>dst</c> are linear and premultiplied.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasBlendMode : IEquatable<CanvasBlendMode>, IHasInvalidDefault {
	private readonly string? name;

	internal BlendState? State { get; }

	private CanvasBlendMode(string name, BlendState? state) {
		this.name = name;
		State = state;
	}

	/// <summary>
	/// <c>src + dst * (1 - src.a)</c> for color and alpha, i.e. a standard alpha blend.
	/// </summary>
	public static readonly CanvasBlendMode Normal = new(nameof(Normal), new BlendState(
		Color: new BlendComponent(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha),
		Alpha: new BlendComponent(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha)
	));

	/// <summary>
	/// <c>src + dst</c> for color and alpha, i.e. an additive blend.
	/// </summary>
	public static readonly CanvasBlendMode Additive = new(nameof(Additive), new BlendState(
		Color: new BlendComponent(BlendOperation.Add, BlendFactor.One, BlendFactor.One),
		Alpha: new BlendComponent(BlendOperation.Add, BlendFactor.One, BlendFactor.One)
	));

	/// <summary>
	/// <c>src + dst * (1 - src)</c> for color, and <c>src.a + dst.a * (1 - src.a)</c> for alpha, i.e.
	/// a typical "screen" blend, brightening the target.
	/// </summary>
	public static readonly CanvasBlendMode Screen = new(nameof(Screen), new BlendState(
		Color: new BlendComponent(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrc),
		Alpha: new BlendComponent(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha)
	));

	/// <summary>
	/// <c>src * dst + dst * (1 - src.a)</c> for color, and <c>src.a + dst.a * (1 - src.a)</c> for
	/// alpha, i.e. an <b>approximation of a multiplicative blend</b>. If the destination is opaque,
	/// this is an exact multiplicative blend; otherwise, the result is darker than it should be.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The correct formula for a multiplicative blend of the color channels is
	/// <c>src * dst + src * (1 - dst.a) + dst * (1 - src.a)</c>, which is not expressible as a
	/// fixed-function blend (i.e. <see cref="BlendState"/>), so an approximation that leaves out the
	/// <c>src * (1 - dst.a)</c> term has to be used. The correct alpha formula is expressible; only
	/// the color one is problematic.
	/// </para>
	/// <para>
	/// That term is never negative, since <c>dst.a</c> is within <c>[0, 1]</c>; for an opaque
	/// destination, the term is 0, so the result ends up being exact, and for a translucent
	/// destination, the term is positive, so omitting it always makes the result darker than the
	/// correct formula's.
	/// </para>
	/// <para>
	/// The largest possible error is with a transparent destination, i.e. <c>dst.a = 0</c>. Since
	/// colors are premultiplied, it follows that <c>dst = 0</c>. The correct formula, substituted,
	/// is <c>src * 0 + src * (1 - 0) + 0 * (1 - src.a)</c>, which simplifies to <c>src</c>; this
	/// approximate, substituted, is <c>src * 0 + 0 * (1 - src.a)</c>, which simplifies to 0. In other
	/// words, the source would pass through unchanged under the correct formula, but here it turns
	/// into black with an alpha of <c>src.a</c>. Intermediate <c>dst.a</c> values interpolate between
	/// the two extreme cases.
	/// </para>
	/// </remarks>
	public static readonly CanvasBlendMode MultiplyOverOpaque = new(nameof(MultiplyOverOpaque), new BlendState(
		Color: new BlendComponent(BlendOperation.Add, BlendFactor.Dst, BlendFactor.OneMinusSrcAlpha),
		Alpha: new BlendComponent(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha)
	));

	/// <summary>
	/// <c>dst - src</c> for color (negative values get clamped to 0), and <c>dst.a</c> for alpha,
	/// i.e. a subtractive blend. Doesn't subtract alpha, since that would be quite useless as a blend
	/// mode.
	/// </summary>
	public static readonly CanvasBlendMode Subtract = new(nameof(Subtract), new BlendState(
		Color: new BlendComponent(BlendOperation.ReverseSubtract, BlendFactor.One, BlendFactor.One),
		Alpha: new BlendComponent(BlendOperation.Add, BlendFactor.Zero, BlendFactor.One)
	));

	/// <summary>
	/// No blending. Source pixels fully replace the destination, including alpha.
	/// </summary>
	public static readonly CanvasBlendMode Opaque = new(nameof(Opaque), null);

	/// <summary>
	/// Creates a blend mode from an arbitrary <see cref="BlendState"/>.
	/// </summary>
	/// <param name="state">Blend state, operating on linear, premultiplied colors.</param>
	/// <remarks>
	/// Keep in mind that <see cref="Canvas"/> blends linear, premultiplied colors;
	/// <paramref name="state"/> is assumed to be suited for them.
	/// </remarks>
	public static CanvasBlendMode Raw(BlendState state) {
		ArgumentInvalidDefaultException.ThrowIfDefault(state);
		return new CanvasBlendMode(nameof(Raw), state);
	}

	public bool Equals(CanvasBlendMode other) => IsDefault == other.IsDefault && State == other.State;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasBlendMode other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(IsDefault, State);
	public static bool operator ==(CanvasBlendMode left, CanvasBlendMode right) => left.Equals(right);
	public static bool operator !=(CanvasBlendMode left, CanvasBlendMode right) => !left.Equals(right);

	public override string ToString() => name switch {
		null => "default(CanvasBlendMode)",
		nameof(Raw) => $"Raw({State})",
		_ => name,
	};

	internal bool IsDefault => name is null;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
