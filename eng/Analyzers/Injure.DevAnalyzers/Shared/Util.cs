// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.DevAnalyzers.Shared;

internal static class Util {
	// ==========================================================================
	// attributes
	public static bool HasAttribute(ISymbol sym, string metadataName) => GetAttribute(sym, metadataName) is not null;

	public static AttributeData? GetAttribute(ISymbol sym, string metadataName) {
		foreach (AttributeData attr in sym.GetAttributes())
			if (metadataNameMatches(attr.AttributeClass, metadataName))
				return attr;
		return null;
	}

	public static IEnumerable<AttributeData> GetAttributes(ISymbol sym, string metadataName) {
		foreach (AttributeData attr in sym.GetAttributes())
			if (metadataNameMatches(attr.AttributeClass, metadataName))
				yield return attr;
	}

	private static bool metadataNameMatches(INamedTypeSymbol? sym, string metadataName) {
		if (sym is null)
			return false;
		return sym.ToDisplayString() == metadataName ||
			sym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + metadataName;
	}

	public static bool GetBoolNamedArgument(AttributeData attr, string name, bool defaultVal) {
		foreach (KeyValuePair<string, TypedConstant> kv in attr.NamedArguments)
			if (kv.Key == name && kv.Value.Value is bool b)
				return b;
		return defaultVal;
	}

	public static bool IsFlagsEnum(INamedTypeSymbol enumSymbol) => HasAttribute(enumSymbol, "System.FlagsAttribute");

	// ==========================================================================
	// locations
	public static Location GetLocation(ISymbol sym, INamedTypeSymbol fallback) =>
		sym.Locations.FirstOrDefault(static l => l.IsInSource) ??
		fallback.Locations.FirstOrDefault(static l => l.IsInSource) ??
		Location.None;

	public static Location GetPrimaryLocation(ISymbol sym) =>
		sym.Locations.FirstOrDefault(static l => l.IsInSource) ?? Location.None;

	public static Location GetAttributeLocation(AttributeData attr, INamedTypeSymbol fallback, CancellationToken ct) =>
		attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? GetPrimaryLocation(fallback);

	// ==========================================================================
	// target validation
	public static bool IsPartial(INamedTypeSymbol sym, CancellationToken ct) {
		foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences)
			if (sr.GetSyntax(ct) is TypeDeclarationSyntax s && s.Modifiers.Any(SyntaxKind.PartialKeyword))
				return true;
		return false;
	}

	// common shape requirements for the generator attributes that target a `readonly partial struct`;
	// reports through mustBeReadonly for a non-readonly struct and through invalidTarget otherwise
	public static bool ValidateReadonlyStructTarget(
		INamedTypeSymbol sym,
		Location loc,
		bool allowNested,
		DiagnosticDescriptor invalidTarget,
		DiagnosticDescriptor mustBeReadonly,
		DiagnosticSink sink,
		CancellationToken ct
	) {
		string? err;
		if (sym.TypeKind != TypeKind.Struct) {
			err = "Target must be a struct.";
		} else if (!IsPartial(sym, ct)) {
			err = "Target must be declared 'partial'.";
		} else if (!sym.IsReadOnly) {
			sink.Report(mustBeReadonly, loc, sym.Name);
			return false;
		} else if (sym.IsRefLikeType) {
			err = "Ref structs are not supported.";
		} else if (sym.IsRecord) {
			err = "'record struct' is not supported.";
		} else if (sym.IsFileLocal) {
			err = "File-local types are not supported.";
		} else if (!allowNested && sym.ContainingType is not null) {
			err = "Nested structs are not supported.";
		} else if (sym.TypeParameters.Length != 0) {
			err = "Generic structs are not supported.";
		} else {
			err = CheckContainingTypes(sym, ct);
		}
		if (err is null)
			return true;
		sink.Report(invalidTarget, loc, err);
		return false;
	}

	// generated code reopens every containing type (see GetTypeHeader), so they all have to be
	// reopenable
	public static string? CheckContainingTypes(INamedTypeSymbol sym, CancellationToken ct) {
		for (INamedTypeSymbol? type = sym.ContainingType; type is not null; type = type.ContainingType) {
			if (type.IsFileLocal)
				return $"Containing type '{type.Name}' is file-local, which is not supported.";
			if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface))
				return $"Containing type '{type.Name}' has unsupported kind '{type.TypeKind}'.";
			if (!IsPartial(type, ct))
				return $"Containing type '{type.Name}' must be declared 'partial'.";
		}
		return null;
	}

	// ==========================================================================
	// enum helpers
	public static INamedTypeSymbol? GetSingleNestedEnum(INamedTypeSymbol sym, string name) {
		INamedTypeSymbol? result = null;
		foreach (INamedTypeSymbol type in sym.GetTypeMembers(name)) {
			if (type.TypeKind != TypeKind.Enum)
				continue;
			if (result is not null)
				return null;
			result = type;
		}
		return result;
	}

	public static bool TryGetEnumMemberUInt64(IFieldSymbol field, out ulong value) {
		switch (field.ConstantValue) {
		case byte x:
			value = x;
			return true;
		case sbyte x:
			value = unchecked((ulong)x);
			return true;
		case short x:
			value = unchecked((ulong)x);
			return true;
		case ushort x:
			value = x;
			return true;
		case int x:
			value = unchecked((ulong)x);
			return true;
		case uint x:
			value = x;
			return true;
		case long x:
			value = unchecked((ulong)x);
			return true;
		case ulong x:
			value = x;
			return true;
		default:
			value = 0;
			return false;
		}
	}

	public static string UInt64Display(ulong value) => value.ToString(CultureInfo.InvariantCulture);

	// ==========================================================================
	// source emission
	public static string EscapeIdentifier(string name) =>
		SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
			? "@" + name
			: name;

	public static string? GetNamespace(INamedTypeSymbol sym) =>
		!sym.ContainingNamespace.IsGlobalNamespace ? sym.ContainingNamespace.ToDisplayString() : null;

	// unique per type even for nested types that share a simple name
	public static string GetHintName(INamedTypeSymbol sym, string suffix) {
		Stack<string> parts = new();
		for (INamedTypeSymbol? type = sym; type is not null; type = type.ContainingType)
			parts.Push(type.Arity == 0 ? type.Name : type.Name + "`" + type.Arity.ToString(CultureInfo.InvariantCulture));
		string? ns = GetNamespace(sym);
		if (ns is not null)
			parts.Push(ns);
		return string.Join(".", parts) + suffix;
	}

	// e.g. `public readonly ref partial struct Foo<T>`; constraints and base lists are left out since
	// partial declarations don't have to repeat them
	public static string GetTypeHeader(INamedTypeSymbol type) {
		type = type.OriginalDefinition;
		if (type.IsFileLocal)
			throw new NotSupportedException($"cannot reopen file-local type '{type.Name}'");

		List<string> modifiers = new(5);
		switch (type.DeclaredAccessibility) {
		case Accessibility.Public:
			modifiers.Add("public");
			break;
		case Accessibility.Internal:
			modifiers.Add("internal");
			break;
		case Accessibility.Private:
			modifiers.Add("private");
			break;
		case Accessibility.Protected:
			modifiers.Add("protected");
			break;
		case Accessibility.ProtectedOrInternal:
			modifiers.Add("protected internal");
			break;
		case Accessibility.ProtectedAndInternal:
			modifiers.Add("private protected");
			break;
		case Accessibility.NotApplicable:
			break;
		default:
			throw new ArgumentOutOfRangeException(nameof(type.DeclaredAccessibility));
		}

		string kind;
		switch (type.TypeKind) {
		case TypeKind.Class:
			if (type.IsRecord) {
				kind = "record class";
				if (type.IsAbstract)
					modifiers.Add("abstract");
				if (type.IsSealed)
					modifiers.Add("sealed");
			} else {
				kind = "class";
				if (type.IsStatic) {
					modifiers.Add("static");
				} else {
					if (type.IsAbstract)
						modifiers.Add("abstract");
					if (type.IsSealed)
						modifiers.Add("sealed");
				}
			}
			break;
		case TypeKind.Struct:
			if (type.IsReadOnly)
				modifiers.Add("readonly");
			if (type.IsRecord) {
				if (type.IsRefLikeType)
					throw new NotSupportedException($"record struct '{type.Name}' is unexpectedly ref-like");
				kind = "record struct";
			} else {
				if (type.IsRefLikeType)
					modifiers.Add("ref");
				kind = "struct";
			}
			break;
		case TypeKind.Interface:
			kind = "interface";
			break;
		default:
			throw new NotSupportedException($"type '{type.Name}' has unsupported kind '{type.TypeKind}'");
		}

		modifiers.Add("partial");

		string typeParameters = type.TypeParameters.Length == 0
			? ""
			: "<" + string.Join(
				", ",
				type.TypeParameters.Select(static param => {
						string variance = param.Variance switch {
							VarianceKind.In => "in ",
							VarianceKind.Out => "out ",
							VarianceKind.None => "",
							_ => throw new ArgumentOutOfRangeException(nameof(param.Variance)),
						};
						return variance + EscapeIdentifier(param.Name);
					}
				)
			) + ">";

		return string.Join(" ", modifiers) + " " + kind + " " + EscapeIdentifier(type.Name) + typeParameters;
	}

	// outermost first
	public static List<string> GetContainingTypeHeaders(INamedTypeSymbol sym) {
		Stack<INamedTypeSymbol> containingTypes = new();
		for (INamedTypeSymbol? type = sym.ContainingType; type is not null; type = type.ContainingType)
			containingTypes.Push(type);
		List<string> result = new(containingTypes.Count);
		while (containingTypes.TryPop(out INamedTypeSymbol? type))
			result.Add(GetTypeHeader(type));
		return result;
	}

	// ==========================================================================
	// misc
	// this is the cuck target framework version where there isn't even a Stack<T>.TryPop yet
	public static bool TryPop<T>(this Stack<T> stack, [MaybeNullWhen(false)] out T value) {
		if (stack.Count == 0) {
			value = default;
			return false;
		}
		value = stack.Pop();
		return true;
	}
}
