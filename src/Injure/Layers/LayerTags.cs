// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.Collections;
using Injure.Mods.CodeAnalysis;

namespace Injure.Layers;

public readonly struct LayerTag : IEquatable<LayerTag> {
	internal readonly ulong RegistryId; // IDs start at 1 so `default` isn't a valid tag
	internal readonly ulong Id; // IDs start at 1 so `default` isn't a valid tag
	internal LayerTag(ulong registryId, ulong id) {
		RegistryId = registryId;
		Id = id;
	}

	public bool Equals(LayerTag other) => RegistryId == other.RegistryId && Id == other.Id;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is LayerTag other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(RegistryId, Id);
	public static bool operator ==(LayerTag left, LayerTag right) => left.Equals(right);
	public static bool operator !=(LayerTag left, LayerTag right) => !left.Equals(right);
}

internal readonly record struct TagKey(ulong NamespaceId, string Name);

public readonly struct LayerTagSet {
	private readonly LayerTag[]? items;

	public static readonly LayerTagSet Empty = default;

	public LayerTagSet(ReadOnlySpan<LayerTag> tags) {
		if (tags.IsEmpty) {
			items = null;
			return;
		}

		LayerTag[] arr = tags.ToArray();
		int count = 0;
		for (int i = 0; i < arr.Length; i++) {
			LayerTag tag = arr[i];
			bool seen = false;
			for (int j = 0; j < count; j++)
				if (arr[j] == tag) {
					seen = true;
					break;
				}
			if (!seen)
				arr[count++] = tag;
		}

		if (count == 0)
			items = null;
		else if (count == arr.Length)
			items = arr;
		else
			items = arr[..count];
	}

	public int Count => items?.Length ?? 0;

	public bool Contains(LayerTag tag) {
		LayerTag[]? arr = items;
		if (arr is null)
			return false;
		for (int i = 0; i < arr.Length; i++)
			if (arr[i] == tag)
				return true;
		return false;
	}

	public bool Intersects(ReadOnlySpan<LayerTag> other) {
		LayerTag[]? arr = items;
		if (arr is null || other.IsEmpty)
			return false;
		for (int i = 0; i < other.Length; i++)
			if (Contains(other[i]))
				return true;
		return false;
	}

	public bool Intersects(in LayerTagSet other) => Intersects(other.AsSpan());

	public ReadOnlySpan<LayerTag> AsSpan() => items is null ? ReadOnlySpan<LayerTag>.Empty : items;
}

public sealed class LayerTagRegistry {
	private static ulong nextRegistryId = 0; // first ID will be 1 since this gets incremented upfront
	internal readonly ulong RegistryId = Interlocked.Increment(ref nextRegistryId);

	private readonly BijectiveMap<string, ulong> namespaces = new(cmpLeft: StringComparer.Ordinal);
	private readonly BijectiveMap<TagKey, LayerTag> tags = new();

	// first will be 1 since these get incremented upfront
	private ulong nextNamespaceId = 0;
	private ulong nextTagId = 0;

	public LayerTag GetOrCreate(string ns, string name) {
		validate(ns, nameof(ns), "layer tag namespace");
		validate(name, nameof(name), "layer tag name");
		ulong nsId = getOrCreateNs(ns);
		TagKey key = new(nsId, name);
		if (tags.TryGetByLeft(key, out LayerTag tag))
			return tag;
		tag = new LayerTag(RegistryId, ++nextTagId);
		tags.Add(key, tag);
		return tag;
	}

	public string GetQualifiedName(LayerTag tag) {
		if (!tags.TryGetByRight(tag, out TagKey key))
			throw new ArgumentException("unknown layer tag", nameof(tag));
		if (!namespaces.TryGetByRight(key.NamespaceId, out string? ns))
			throw new ArgumentException("unknown layer tag", nameof(tag));
		return ns + "::" + key.Name;
	}

	private ulong getOrCreateNs(string ns) {
		if (namespaces.TryGetByLeft(ns, out ulong id))
			return id;
		id = ++nextNamespaceId;
		namespaces.Add(ns, id);
		return id;
	}

	private static void validate(ReadOnlySpan<char> s, string paramName, string kind) {
		if (s.IsEmpty)
			throw new ArgumentException(kind + " must not be empty", paramName);
		if (!char.IsAsciiLetterOrDigit(s[0]))
			throw new ArgumentException(kind + " must start with an ASCII letter or ASCII digit", paramName);
		foreach (char c in s)
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
				throw new ArgumentException(
					$"{kind} contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.')",
					paramName
				);
	}
}
