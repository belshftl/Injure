// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Computes a <see cref="DerivedAsset{T}"/>'s value from the current versions of its inputs.
/// </summary>
/// <typeparam name="T">Type of the value. Must be a reference type.</typeparam>
/// <param name="ctx">Gives access to the inputs' values.</param>
/// <returns>The new value, owned by the asset store afterwards.</returns>
public delegate T DeriveFunc<T>(DeriveCtx ctx) where T : class;

/// <summary>
/// Passed to a <see cref="DeriveFunc{T}"/> to give access to the values of the inputs it's
/// computing from.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly ref struct DeriveCtx {
	private readonly Versioned[]? inputs;
	private readonly AssetStore.AssetVersion[]? inputVersions;

	internal DeriveCtx(Versioned[] inputs, AssetStore.AssetVersion[] inputVersions) {
		this.inputs = inputs;
		this.inputVersions = inputVersions;
	}

	/// <summary>
	/// The value of <paramref name="input"/> this computation is based on.
	/// </summary>
	/// <typeparam name="TInput">Type of the input's value.</typeparam>
	/// <param name="input">One of the inputs the derived value was created with.</param>
	/// <returns>
	/// The value; the derived version keeps it alive for as long as it's alive itself.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="input"/> isn't one of the derived value's inputs.
	/// </exception>
	public TInput Get<TInput>(Versioned<TInput> input) where TInput : class {
		if (inputs is null)
			throw InvalidDefaultException.For<DeriveCtx>();
		for (int i = 0; i < inputs.Length; i++)
			if (ReferenceEquals(inputs[i], input))
				return ((AssetStore.AssetVersion<TInput>)inputVersions![i]).Value;
		throw new ArgumentException("not a input of the derived value being computed", nameof(input));
	}
}
