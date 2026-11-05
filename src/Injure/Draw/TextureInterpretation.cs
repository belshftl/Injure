// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Draw;

/// <summary>
/// Describes how <see cref="Canvas"/> interprets sampled texture data.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Rmask</c>/<c>Sdf</c> interpretations are temporarily removed until the material system
/// rework.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TextureInterpretation {
	/// <summary>Raw switch tag for <see cref="TextureInterpretation"/>.</summary>
	public enum Case {
		/// <summary>
		/// Interprets the texture as ordinary color data.
		/// </summary>
		Color = 1,

	}
}
