// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Injure.ModKit.Abstractions.ManifestReader;

public sealed class JsonObjectReader {
	private readonly JNode node;
	private IReadOnlyDictionary<string, JProperty> props => node.Properties ?? throw new InternalStateException("bad object node");

	private static readonly Dictionary<Type, object> enumMaps = new();
	private static readonly Lock enumMapsLock = new();

	public JsonObjectReader(JNode node) {
		if (node.Kind != JKind.Object)
			throw new ManifestReadException(node.NodePath, node.Span, "expected object");
		this.node = node;
	}

	public void RejectUnknownProperties(params string[] allowed) {
		HashSet<string> allowedSet = new(allowed, StringComparer.Ordinal);
		foreach (JProperty prop in props.Values)
			if (!allowedSet.Contains(prop.Name))
				throw new ManifestReadException(prop.Value.NodePath, prop.NameSpan, $"unknown property '{prop.Name}'");
	}

	public bool Has(string name) => props.ContainsKey(name);

	public JNode RequiredNode(string name) => required(name);

	public string RequiredString(string name) {
		JNode val = required(name);
		if (val.Kind != JKind.String)
			throw new ManifestReadException(val.NodePath, val.Span, "must be a string");
		return val.StringValue ?? "";
	}

	public string? OptionalString(string name) {
		if (!tryGet(name, out JNode? val))
			return null;
		if (val.Kind == JKind.Null)
			return null;
		if (val.Kind != JKind.String)
			throw new ManifestReadException(val.NodePath, val.Span, "must be a string");
		return val.StringValue;
	}

	public bool OptionalBool(string name, bool defaultValue = false) {
		if (!tryGet(name, out JNode? val))
			return defaultValue;
		if (val.Kind != JKind.Bool)
			throw new ManifestReadException(val.NodePath, val.Span, "must be a bool");
		return val.BoolValue;
	}

	public int RequiredInt(string name) {
		JNode val = required(name);
		if (val.Kind != JKind.Number || val.NumberText is null || !int.TryParse(val.NumberText, NumberStyles.None, CultureInfo.InvariantCulture, out int result))
			throw new ManifestReadException(val.NodePath, val.Span, "must be an integer");
		return result;
	}

	public T RequiredEnum<T>(string name) where T : struct, Enum {
		string raw = RequiredString(name);
		IReadOnlyDictionary<string, T> map = getEnumMap<T>();
		if (map.TryGetValue(raw, out T val))
			return val;
		string allowed = string.Join(", ", map.Keys);
		JNode jval = props[name].Value;
		throw new ManifestReadException(jval.NodePath, jval.Span, $"invalid {typeof(T).Name} value '{raw}'; expected one of: {allowed}");
	}

	public T OptionalEnum<T>(string name, T defaultValue) where T : struct, Enum {
		if (!tryGet(name, out JNode? jval))
			return defaultValue;
		if (jval.Kind != JKind.String)
			throw new ManifestReadException(jval.NodePath, jval.Span, "must be a string");

		string raw = jval.StringValue ?? "";
		IReadOnlyDictionary<string, T> map = getEnumMap<T>();
		if (map.TryGetValue(raw, out T val))
			return val;
		string allowed = string.Join(", ", map.Keys);
		throw new ManifestReadException(jval.NodePath, jval.Span, $"invalid {typeof(T).Name} value '{raw}'; expected one of: {allowed}");
	}

	public JsonObjectReader RequiredObject(string name) {
		JNode val = required(name);
		if (val.Kind != JKind.Object)
			throw new ManifestReadException(val.NodePath, val.Span, "must be an object");
		return new JsonObjectReader(val);
	}

	public JsonObjectReader? OptionalObject(string name) {
		if (!tryGet(name, out JNode? val))
			return null;
		if (val.Kind == JKind.Null)
			return null;
		if (val.Kind != JKind.Object)
			throw new ManifestReadException(val.NodePath, val.Span, "must be an object");
		return new JsonObjectReader(val);
	}

	public IReadOnlyList<JNode> OptionalArray(string name) {
		if (!tryGet(name, out JNode? val))
			return Array.Empty<JNode>();
		if (val.Kind != JKind.Array)
			throw new ManifestReadException(val.NodePath, val.Span, "must be an array");
		return val.Items ?? Array.Empty<JNode>();
	}

	private bool tryGet(string name, [NotNullWhen(true)] out JNode? value) {
		if (!props.TryGetValue(name, out JProperty? prop)) {
			value = null;
			return false;
		}
		value = prop.Value;
		return true;
	}

	private JNode required(string name) {
		if (!tryGet(name, out JNode? value))
			throw new ManifestReadException(node.NodePath + "." + name, node.Span, $"required property '{name}' is missing");
		return value;
	}

	private static IReadOnlyDictionary<string, T> getEnumMap<T>() where T : struct, Enum {
		lock (enumMapsLock) {
			if (enumMaps.TryGetValue(typeof(T), out object? cached))
				return (IReadOnlyDictionary<string, T>)cached;
			Dictionary<string, T> map = new(StringComparer.Ordinal);
			foreach (T value in Enum.GetValues<T>()) {
				string name = Enum.GetName(value) ?? throw new InternalStateException($"bad enum value '{value}' for '{typeof(T).FullName}'");
				string kebab = ToKebab(name);
				if (!map.TryAdd(kebab, value))
					throw new InternalStateException($"duplicate kebab enum name '{kebab}' in '{typeof(T).FullName}'");
			}
			enumMaps.Add(typeof(T), map);
			return map;
		}
	}

	internal static string ToKebab(string s) {
		StringBuilder sb = new();
		for (int i = 0; i < s.Length; i++) {
			char ch = s[i];
			if (char.IsUpper(ch)) {
				bool hasPrev = i > 0;
				bool hasNext = i + 1 < s.Length;
				bool prevIsLowerOrDigit = hasPrev && (char.IsLower(s[i - 1]) || char.IsDigit(s[i - 1]));
				bool nextIsLower = hasNext && char.IsLower(s[i + 1]);
				if (hasPrev && (prevIsLowerOrDigit || nextIsLower))
					sb.Append('-');
				sb.Append(char.ToLowerInvariant(ch));
			} else {
				sb.Append(ch);
			}
		}
		return sb.ToString();
	}
}
