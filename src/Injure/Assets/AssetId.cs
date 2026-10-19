// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Injure.Mods;

namespace Injure.Assets;

/// <summary>
/// Unique asset identifier, consisting of a namespace and a path, written as <c>ns::path</c>.
/// </summary>
/// <remarks>
/// <para>
/// The namespace is a valid owner ID, typically that of whoever provides the asset (see
/// <see cref="ValidateNamespace(ReadOnlySpan{char}, out string?)"/>). The path is a
/// forward-slash-separated path whose rules are looser, so that it can mirror file paths (see
/// <see cref="ValidatePath(ReadOnlySpan{char}, out string?)"/>).
/// </para>
/// <para>
/// Equality is ordinal, so both parts are case-sensitive.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[JsonConverter(typeof(AssetIdJsonConverter))]
public readonly struct AssetId : IEquatable<AssetId>, ISpanParsable<AssetId>, IHasInvalidDefault {
	// both are never null for a valid value
	private readonly string? ns;
	private readonly string? path;

	/// <summary>
	/// Namespace portion of the asset ID.
	/// </summary>
	public string Namespace => ns ?? throw InvalidDefaultException.For<AssetId>();

	/// <summary>
	/// Forward-slash-separated path portion of the asset ID.
	/// </summary>
	public string Path => path ?? throw InvalidDefaultException.For<AssetId>();

	/// <summary>
	/// Creates an asset ID from a namespace and path.
	/// </summary>
	/// <param name="ns">Asset namespace.</param>
	/// <param name="path">Asset path.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ns"/> or <paramref name="path"/> is not valid.
	/// </exception>
	public AssetId(string ns, string path) : this(ns, path, skipValidation: false) {
	}

	internal AssetId(string ns, string path, bool skipValidation) {
		if (!skipValidation) {
			if (!ValidateNamespace(ns, out string? err))
				throw new ArgumentException(err, nameof(ns));
			if (!ValidatePath(path, out err))
				throw new ArgumentException(err, nameof(path));
		}
		this.ns = ns;
		this.path = path;
	}

	public bool Equals(AssetId other) => ns == other.ns && path == other.path;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is AssetId other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(ns, path);
	public static bool operator ==(AssetId left, AssetId right) => left.Equals(right);
	public static bool operator !=(AssetId left, AssetId right) => !left.Equals(right);

	/// <summary>
	/// Returns the canonical <c>ns::path</c> string form, or <c>default(AssetId)</c> for the
	/// <see langword="default"/> value.
	/// </summary>
	public override string ToString() => ns is null ? "default(AssetId)" : $"{ns}::{path}";

#pragma warning disable CS1573 // parameter has no matching <param> tag (silenced because it seems to be a false-positive with inheritdoc)

	/// <summary>
	/// Parses an <see cref="AssetId"/> from the canonical <c>ns::path</c> string form.
	/// </summary>
	/// <param name="s">String to parse.</param>
	/// <param name="val">On success, the parsed value.</param>
	/// <returns>
	/// <see langword="true"/> if the string is in the format <c>ns::path</c> and has a valid namespace
	/// and path, and as such the parse succeeded, otherwise <see langword="false"/>.
	/// </returns>
	public static bool TryParse([NotNullWhen(true)] string? s, out AssetId val) {
		val = default;
		if (s is null)
			return false;
		int i = s.IndexOf("::", StringComparison.Ordinal);
		if (i < 0 || s.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
			return false;
		if (!ValidateNamespace(s.AsSpan(0, i), out _) || !ValidatePath(s.AsSpan(i + 2), out _))
			return false;
		val = new AssetId(s[..i], s[(i + 2)..], skipValidation: true);
		return true;
	}

	/// <inheritdoc cref="TryParse(string, out AssetId)"/>
	/// <param name="provider">Ignored.</param>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AssetId val) => TryParse(s, out val);

	/// <summary>
	/// Parses an <see cref="AssetId"/> from the canonical <c>ns::path</c> string form.
	/// </summary>
	/// <param name="s">String to parse.</param>
	/// <returns>
	/// The parsed value.
	/// </returns>
	/// <exception cref="FormatException">
	/// Thrown if the string is not in the format <c>ns::path</c>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the string has an invalid namespace or path component.
	/// </exception>
	public static AssetId Parse([NotNull] string? s) {
		ArgumentNullException.ThrowIfNull(s);
		int i = s.IndexOf("::", StringComparison.Ordinal);
		if (i < 0 || s.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
			throw new FormatException("string must contain exactly one occurrence of ::");
		ValidateNamespaceOrThrow(s.AsSpan(0, i));
		ValidatePathOrThrow(s.AsSpan(i + 2));
		return new AssetId(s[..i], s[(i + 2)..], skipValidation: true);
	}

	/// <inheritdoc cref="Parse(string)"/>
	/// <param name="provider">Ignored.</param>
	public static AssetId Parse([NotNull] string? s, IFormatProvider? provider) => Parse(s);

	/// <summary>
	/// Parses an <see cref="AssetId"/> from the canonical <c>ns::path</c> string form.
	/// </summary>
	/// <param name="span">Span of characters to parse.</param>
	/// <param name="val">On success, the parsed value.</param>
	/// <returns>
	/// <see langword="true"/> if the string is in the format <c>ns::path</c> and has a valid namespace
	/// and path, and as such the parse succeeded, otherwise <see langword="false"/>.
	/// </returns>
	public static bool TryParse(ReadOnlySpan<char> span, out AssetId val) {
		val = default;
		int i = span.IndexOf("::");
		if (i < 0 || span[(i + 2)..].IndexOf("::") >= 0)
			return false;
		if (!ValidateNamespace(span[..i], out _) || !ValidatePath(span[(i + 2)..], out _))
			return false;
		val = new AssetId(new string(span[..i]), new string(span[(i + 2)..]), skipValidation: true);
		return true;
	}

	/// <inheritdoc cref="TryParse(ReadOnlySpan{char}, out AssetId)"/>
	/// <param name="provider">Ignored.</param>
	public static bool TryParse(ReadOnlySpan<char> span, IFormatProvider? provider, out AssetId val) => TryParse(span, out val);

	/// <summary>
	/// Parses an <see cref="AssetId"/> from the canonical <c>ns::path</c> string form.
	/// </summary>
	/// <param name="span">Span of characters to parse.</param>
	/// <returns>
	/// The parsed value.
	/// </returns>
	/// <exception cref="FormatException">
	/// Thrown if the string is not in the format <c>ns::path</c>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the string has an invalid namespace or path component.
	/// </exception>
	public static AssetId Parse(ReadOnlySpan<char> span) {
		int i = span.IndexOf("::");
		if (i < 0 || span[(i + 2)..].IndexOf("::") >= 0)
			throw new FormatException("string must contain exactly one occurrence of ::");
		ValidateNamespaceOrThrow(span[..i]);
		ValidatePathOrThrow(span[(i + 2)..]);
		return new AssetId(new string(span[..i]), new string(span[(i + 2)..]), skipValidation: true);
	}

	/// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
	/// <param name="provider">Ignored.</param>
	public static AssetId Parse(ReadOnlySpan<char> span, IFormatProvider? provider) => Parse(span);

#pragma warning restore CS1573 // parameter has no matching <param> tag (silenced because it seems to be a false-positive with inheritdoc)

	// ==========================================================================
	// validation

	/// <summary>
	/// Checks an asset namespace for validity.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if <paramref name="s"/> is a valid asset namespace;
	/// <see langword="false"/> otherwise.
	/// </returns>
	/// <param name="s">String to perform the check on.</param>
	/// <param name="err">
	/// If the method returned <see langword="false"/>, an error string describing the invalidity.
	/// </param>
	/// <remarks>
	/// Valid asset namespaces are exactly the valid owner IDs; see
	/// <see cref="ModMetadataValidation.ValidateOwnerId(ReadOnlySpan{char}, out string?)"/>.
	/// </remarks>
	public static bool ValidateNamespace(ReadOnlySpan<char> s, [NotNullWhen(false)] out string? err) {
		if (ModMetadataValidation.ValidateOwnerId(s, out string? e)) {
			err = null;
			return true;
		}
		err = $"asset namespace must be a valid owner ID: {e}";
		return false;
	}

	/// <summary>
	/// Checks an asset namespace for validity, throwing <see cref="ArgumentException"/> if it's
	/// invalid.
	/// </summary>
	/// <inheritdoc cref="ValidateNamespace(ReadOnlySpan{char}, out string?)"/>
	public static void ValidateNamespaceOrThrow(ReadOnlySpan<char> s) {
		if (!ValidateNamespace(s, out string? err))
			throw new ArgumentException(err);
	}

	/// <summary>
	/// Checks an asset path for validity.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if <paramref name="s"/> is a valid asset path; <see langword="false"/>
	/// otherwise.
	/// </returns>
	/// <param name="s">String to perform the check on.</param>
	/// <param name="err">
	/// If the method returned <see langword="false"/>, an error string describing the invalidity.
	/// </param>
	/// <remarks>
	/// <para>
	/// A valid path is non-empty, uses <c>/</c> as its separator, doesn't start or end with <c>/</c>
	/// or whitespace, has no empty, <c>.</c>, or <c>..</c> segments, and contains no control
	/// characters, <c>\</c>, or <c>::</c> (which separates the namespace from the path).
	/// </para>
	/// <para>
	/// These rules deliberately differ from local IDs (see
	/// <see cref="ModMetadataValidation.ValidateLocalId(ReadOnlySpan{char}, out string?)"/>): paths
	/// can contain whitespace, non-ASCII characters, and most punctuation, since they often mirror
	/// file paths. They may become stricter before the API stabilizes.
	/// </para>
	/// </remarks>
	public static bool ValidatePath(ReadOnlySpan<char> s, [NotNullWhen(false)] out string? err) {
		static bool checkSeg(ReadOnlySpan<char> seg, [NotNullWhen(false)] out string? err) {
			if (seg.IsEmpty) {
				err = "asset path must not contain empty path segments";
				return false;
			}
			if (seg.SequenceEqual(".")) {
				err = "asset path must not contain '.' path segments";
				return false;
			}
			if (seg.SequenceEqual("..")) {
				err = "asset path must not contain '..' path segments";
				return false;
			}
			err = null;
			return true;
		}

		if (s.IsEmpty) {
			err = "asset path must not be empty";
			return false;
		}
		if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) {
			err = "asset path must not have leading/trailing whitespace";
			return false;
		}
		if (s[0] == '/') {
			err = "asset path must not start with '/' (rooted to... what?)";
			return false;
		}
		if (s[^1] == '/') {
			err = "asset path must not end with '/'";
			return false;
		}
		if (s.Contains("::", StringComparison.Ordinal)) {
			err = "asset path must not contain '::', which separates the namespace from the path";
			return false;
		}
		int segStart = 0;
		for (int i = 0; i < s.Length; i++) {
			char c = s[i];
			if (char.IsControl(c)) {
				err = "asset path must not contain control characters";
				return false;
			}
			if (c == '\\') {
				err = "asset path must use '/' as the path separator, not '\\'";
				return false;
			}
			if (c == '/') {
				ReadOnlySpan<char> seg = s[segStart..i];
				if (!checkSeg(seg, out err))
					return false;
				segStart = i + 1;
			}
		}
		ReadOnlySpan<char> lastSeg = s[segStart..];
		if (!checkSeg(lastSeg, out err))
			return false;
		err = null;
		return true;
	}

	/// <summary>
	/// Checks an asset path for validity, throwing <see cref="ArgumentException"/> if it's invalid.
	/// </summary>
	/// <inheritdoc cref="ValidatePath(ReadOnlySpan{char}, out string?)"/>
	public static void ValidatePathOrThrow(ReadOnlySpan<char> s) {
		if (!ValidatePath(s, out string? err))
			throw new ArgumentException(err);
	}

	bool IHasInvalidDefault.IsDefault => ns is null;
}

/// <summary>
/// <see cref="JsonConverter{T}"/> for <see cref="AssetId"/>s.
/// </summary>
public sealed class AssetIdJsonConverter : JsonConverter<AssetId> {
	/// <summary>
	/// Reads a JSON string value and parses it with <see cref="AssetId.Parse(string?)"/>.
	/// </summary>
	public override AssetId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => AssetId.Parse(reader.GetString());

	/// <summary>
	/// Writes an <see cref="AssetId"/> as a JSON string value using <see cref="AssetId.ToString()"/>.
	/// </summary>
	public override void Write(Utf8JsonWriter writer, AssetId val, JsonSerializerOptions options) {
		ArgumentInvalidDefaultException.ThrowIfDefault(val);
		writer.WriteStringValue(val.ToString());
	}
}

/// <summary>
/// An asset ID paired with an asset type, which identifies one <see cref="Asset{T}"/> of a store.
/// </summary>
/// <param name="AssetId">The asset ID.</param>
/// <param name="AssetType">The asset type.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct AssetKey(AssetId AssetId, Type AssetType) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
