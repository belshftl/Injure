// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Draw;

/// <summary>
/// How <see cref="Canvas"/> renders textured draws.
/// </summary>
/// <remarks>
/// <para>
/// Untextured draws ignore the material. <see cref="Color"/> is currently the only one; the others
/// have been temporarily removed until the material system rework.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasMaterial : IEquatable<CanvasMaterial>, IHasInvalidDefault {
	private readonly TextureInterpretation textureInterpretation;

	private CanvasMaterial(TextureInterpretation textureInterpretation) {
		this.textureInterpretation = textureInterpretation;
	}

	/// <summary>
	/// A material that draws textures as color, i.e. the premultiplied, sRGB-encoded color an
	/// <see cref="SrgbTexture2d"/> or <see cref="SrgbRenderTarget2d"/> holds.
	/// </summary>
	public static readonly CanvasMaterial Color = new(TextureInterpretation.Color);

	/// <summary>
	/// How sampled texture data is interpreted.
	/// </summary>
	public TextureInterpretation TextureInterpretation =>
		IsDefault ? throw InvalidDefaultException.For<CanvasMaterial>() : textureInterpretation;

	public bool Equals(CanvasMaterial other) => textureInterpretation == other.textureInterpretation;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasMaterial other && Equals(other);
	public override int GetHashCode() => textureInterpretation.RawTag.GetHashCode();
	public static bool operator ==(CanvasMaterial left, CanvasMaterial right) => left.Equals(right);
	public static bool operator !=(CanvasMaterial left, CanvasMaterial right) => !left.Equals(right);

	internal bool IsDefault => textureInterpretation == default;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
