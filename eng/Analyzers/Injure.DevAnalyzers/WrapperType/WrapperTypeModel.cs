// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.DevAnalyzers.WrapperType;

using Diag = Diagnostics.WrapperType;

// accessibility strings are "public" or "internal"; Modifier is "", "new " or "override " depending
// on what the forwarded member would hide in the wrapper's base types
internal abstract record ForwardedMember(string Accessibility, string Modifier);

internal sealed record ForwardedMethod(IMethodSymbol Method, string Accessibility, string Modifier) : ForwardedMember(Accessibility, Modifier);

// at least one of GetAccessibility/SetAccessibility is non-null, and Accessibility is the broader one
internal sealed record ForwardedProperty(
	IPropertySymbol Property,
	string Accessibility,
	string? GetAccessibility,
	string? SetAccessibility,
	string Modifier
) : ForwardedMember(Accessibility, Modifier);

internal sealed class WrapperTypeModel(INamedTypeSymbol target, INamedTypeSymbol wrapped, ImmutableArray<ForwardedMember> members) {
	public INamedTypeSymbol Target { get; } = target;
	public INamedTypeSymbol Wrapped { get; } = wrapped;
	public ImmutableArray<ForwardedMember> Members { get; } = members;

	// a default struct wrapper holds null, so member access goes through a null check there
	public bool NeedsCheckedAccessor => Target.IsValueType && Wrapped.IsReferenceType;

	// returns null if any error was reported
	public static WrapperTypeModel? Validate(
		INamedTypeSymbol sym,
		AttributeData attr,
		Compilation compilation,
		DiagnosticSink sink,
		CancellationToken ct
	) {
		Location loc = Util.GetAttributeLocation(attr, sym, ct);
		if (!validateTarget(sym, loc, sink, ct))
			return null;
		foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences) {
			if (sr.GetSyntax(ct) is not TypeDeclarationSyntax decl)
				continue;
			foreach (MemberDeclarationSyntax member in decl.Members)
				sink.Report(Diag.WrapperTypeInvalidSourceShape, member.GetLocation(), $"WrapperType target '{sym.Name}' must not declare any members.");
		}

		if (attr.ConstructorArguments.Length != 2) {
			sink.Report(Diag.WrapperTypeInvalidWrappedType, loc, "Attribute must have a typeof(...) argument followed by member names.");
			return null;
		}
		INamedTypeSymbol? wrapped = validateWrappedType(sym, attr.ConstructorArguments[0], compilation, loc, sink);
		if (wrapped is null)
			return null;

		TypedConstant namesArg = attr.ConstructorArguments[1];
		if (namesArg.Kind != TypedConstantKind.Array || namesArg.IsNull || namesArg.Values.Length == 0) {
			sink.Report(Diag.WrapperTypeInvalidMember, loc, "At least one member name must be given.");
			return null;
		}
		ImmutableArray<Location> nameLocs = getNameLocations(attr, namesArg.Values.Length, loc, ct);
		ImmutableArray<ForwardedMember>.Builder members = ImmutableArray.CreateBuilder<ForwardedMember>();
		HashSet<string> seenNames = new(StringComparer.Ordinal);
		for (int i = 0; i < namesArg.Values.Length; i++) {
			if (namesArg.Values[i].Value is not string name || name.Length == 0) {
				sink.Report(Diag.WrapperTypeInvalidMember, nameLocs[i], "Member names must not be null or empty.");
				continue;
			}
			if (!seenNames.Add(name)) {
				sink.Report(Diag.WrapperTypeInvalidMember, nameLocs[i], $"Member '{name}' is listed more than once.");
				continue;
			}
			if (Constants.WrapperType.ReservedMemberNames.Contains(name) || name == sym.Name) {
				sink.Report(Diag.WrapperTypeInvalidMember, nameLocs[i], $"Member name '{name}' is reserved by generated WrapperType code or the wrapper type itself.");
				continue;
			}
			collectForwarded(sym, wrapped, name, compilation, nameLocs[i], sink, members);
		}
		return sink.HasErrors ? null : new WrapperTypeModel(sym, wrapped, members.ToImmutable());
	}

	private static bool validateTarget(INamedTypeSymbol sym, Location loc, DiagnosticSink sink, CancellationToken ct) {
		string? err;
		if (sym.TypeKind is not (TypeKind.Class or TypeKind.Struct)) {
			err = "Target must be a class or struct.";
		} else if (!Util.IsPartial(sym, ct)) {
			err = "Target must be declared 'partial'.";
		} else if (sym.TypeKind == TypeKind.Struct && !sym.IsReadOnly) {
			sink.Report(Diag.WrapperTypeMustBeReadonly, loc, sym.Name);
			return false;
		} else if (sym.IsRecord) {
			err = "Records are not supported.";
		} else if (sym.IsStatic) {
			err = "Static classes are not supported.";
		} else if (sym.IsAbstract) {
			err = "Abstract classes are not supported.";
		} else if (sym.IsFileLocal) {
			err = "File-local types are not supported.";
		} else if (sym.TypeParameters.Length != 0) {
			err = "Generic types are not supported.";
		} else if (hasPrimaryConstructor(sym, ct)) {
			err = "Primary constructors are not supported.";
		} else {
			err = Util.CheckContainingTypes(sym, ct);
		}
		if (err is null)
			return true;
		sink.Report(Diag.WrapperTypeInvalidTarget, loc, err);
		return false;
	}

	private static bool hasPrimaryConstructor(INamedTypeSymbol sym, CancellationToken ct) {
		foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences)
			if (sr.GetSyntax(ct) is TypeDeclarationSyntax { ParameterList: not null })
				return true;
		return false;
	}

	private static INamedTypeSymbol? validateWrappedType(
		INamedTypeSymbol sym,
		TypedConstant arg,
		Compilation compilation,
		Location loc,
		DiagnosticSink sink
	) {
		if (arg.Kind != TypedConstantKind.Type || arg.Value is not INamedTypeSymbol wrapped) {
			sink.Report(Diag.WrapperTypeInvalidWrappedType, loc, "Wrapped type must be a named type.");
			return null;
		}
		if (wrapped.TypeKind == TypeKind.Error)
			return null; // already a compiler error
		string? err = null;
		if (wrapped.IsUnboundGenericType)
			err = $"Wrapped type '{wrapped.ToDisplayString()}' must not be an unbound generic type.";
		else if (wrapped.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct or TypeKind.Delegate))
			err = $"Wrapped type '{wrapped.ToDisplayString()}' must be a class, interface, delegate, or struct.";
		else if (wrapped.IsStatic)
			err = $"Wrapped type '{wrapped.ToDisplayString()}' must not be a static class.";
		else if (SymbolEqualityComparer.Default.Equals(wrapped, sym))
			err = "A type cannot wrap itself.";
		else if (wrapped.IsValueType && !wrapped.IsReadOnly)
			// otherwise every forwarded call operates on a defensive copy of the readonly field
			err = $"Wrapped struct '{wrapped.ToDisplayString()}' must be a readonly struct.";
		else if (wrapped.IsRefLikeType && !sym.IsRefLikeType)
			err = $"Wrapped type '{wrapped.ToDisplayString()}' is a ref struct, so the wrapper must be a ref struct too.";
		else if (!compilation.IsSymbolAccessibleWithin(wrapped, sym))
			err = $"Wrapped type '{wrapped.ToDisplayString()}' is not accessible from '{sym.Name}'.";
		if (err is null)
			return wrapped;
		sink.Report(Diag.WrapperTypeInvalidWrappedType, loc, err);
		return null;
	}

	// points at the individual name arguments if they were passed in expanded form, otherwise at the
	// whole attribute
	private static ImmutableArray<Location> getNameLocations(AttributeData attr, int count, Location fallback, CancellationToken ct) {
		SeparatedSyntaxList<AttributeArgumentSyntax>? args = (attr.ApplicationSyntaxReference?.GetSyntax(ct) as AttributeSyntax)?.ArgumentList?.Arguments;
		ImmutableArray<Location>.Builder result = ImmutableArray.CreateBuilder<Location>(count);
		for (int i = 0; i < count; i++)
			result.Add(args is { } a && a.Count == count + 1 ? a[i + 1].GetLocation() : fallback);
		return result.MoveToImmutable();
	}

	// ==========================================================================
	// member lookup
	private static void collectForwarded(
		INamedTypeSymbol sym,
		INamedTypeSymbol wrapped,
		string name,
		Compilation compilation,
		Location loc,
		DiagnosticSink sink,
		ImmutableArray<ForwardedMember>.Builder result
	) {
		List<ISymbol> candidates = lookup(wrapped, name, compilation);
		if (candidates.Count == 0) {
			sink.Report(Diag.WrapperTypeInvalidMember, loc, $"Type '{wrapped.ToDisplayString()}' has no member named '{name}'.");
			return;
		}

		string? firstProblem = null;
		int added = 0;
		foreach (ISymbol candidate in candidates) {
			string? problem = candidate switch {
				IMethodSymbol m => tryForwardMethod(sym, wrapped, m, compilation, result),
				IPropertySymbol p => tryForwardProperty(sym, wrapped, p, compilation, result),
				_ => $"Member '{name}' of '{wrapped.ToDisplayString()}' is not a property or method; only properties and methods can be forwarded.",
			};
			if (problem is null)
				added++;
			else
				firstProblem ??= problem;
		}
		// overloads that can't be forwarded (static or inaccessible ones, mostly) are skipped as long as
		// at least one can be
		if (added == 0)
			sink.Report(Diag.WrapperTypeInvalidMember, loc, firstProblem);
	}

	// most-derived first; overridden members and members with an already-seen signature are dropped
	private static List<ISymbol> lookup(INamedTypeSymbol wrapped, string name, Compilation compilation) {
		List<INamedTypeSymbol> hierarchy = new();
		if (wrapped.TypeKind == TypeKind.Interface) {
			hierarchy.Add(wrapped);
			hierarchy.AddRange(wrapped.AllInterfaces);
			hierarchy.Add(compilation.ObjectType);
		} else {
			for (INamedTypeSymbol? type = wrapped; type is not null; type = type.BaseType)
				hierarchy.Add(type);
		}

		List<ISymbol> result = new();
		HashSet<ISymbol> overridden = new(SymbolEqualityComparer.Default);
		HashSet<string> seenSignatures = new(StringComparer.Ordinal);
		foreach (INamedTypeSymbol type in hierarchy) {
			foreach (ISymbol member in type.GetMembers(name)) {
				if (member.IsImplicitlyDeclared || overridden.Contains(member.OriginalDefinition))
					continue;
				switch (member) {
				case IMethodSymbol m:
					for (IMethodSymbol? o = m.OverriddenMethod; o is not null; o = o.OverriddenMethod)
						overridden.Add(o.OriginalDefinition);
					break;
				case IPropertySymbol p:
					for (IPropertySymbol? o = p.OverriddenProperty; o is not null; o = o.OverriddenProperty)
						overridden.Add(o.OriginalDefinition);
					break;
				}
				if (seenSignatures.Add(signatureKey(member)))
					result.Add(member);
			}
		}
		return result;
	}

	private static string signatureKey(ISymbol member) => member switch {
		IMethodSymbol m => "M`" + m.Arity + "(" + string.Join(",", m.Parameters.Select(static p => p.RefKind + " " + p.Type.ToDisplayString())) + ")",
		IPropertySymbol p when p.IsIndexer => "I(" + string.Join(",", p.Parameters.Select(static p => p.RefKind + " " + p.Type.ToDisplayString())) + ")",
		_ => member.Kind.ToString(),
	};

	private static string? tryForwardMethod(
		INamedTypeSymbol sym,
		INamedTypeSymbol wrapped,
		IMethodSymbol m,
		Compilation compilation,
		ImmutableArray<ForwardedMember>.Builder result
	) {
		string desc = $"Method '{m.ToDisplayString()}'";
		if (m.MethodKind != MethodKind.Ordinary)
			return $"{desc} is not an ordinary method.";
		if (m.IsStatic)
			return $"{desc} is static; only instance members can be forwarded.";
		if (forwardedAccessibility(m, sym, wrapped, compilation) is not string access)
			return $"{desc} is not accessible from '{sym.Name}'.";
		result.Add(new ForwardedMethod(m, access, getHidingModifier(sym, m, compilation)));
		return null;
	}

	private static string? tryForwardProperty(
		INamedTypeSymbol sym,
		INamedTypeSymbol wrapped,
		IPropertySymbol p,
		Compilation compilation,
		ImmutableArray<ForwardedMember>.Builder result
	) {
		string desc = $"Property '{p.ToDisplayString()}'";
		if (p.IsIndexer)
			return $"{desc} is an indexer, which is not supported.";
		if (p.IsStatic)
			return $"{desc} is static; only instance members can be forwarded.";
		string? get = p.GetMethod is IMethodSymbol g ? forwardedAccessibility(g, sym, wrapped, compilation) : null;
		// init accessors can't be called on an existing instance
		string? set = p.SetMethod is IMethodSymbol { IsInitOnly: false } s && !p.ReturnsByRef && !p.ReturnsByRefReadonly
			? forwardedAccessibility(s, sym, wrapped, compilation)
			: null;
		if (get is null && set is null)
			return $"{desc} has no accessible get or set accessor.";
		string access = get == "public" || set == "public" ? "public" : "internal";
		result.Add(new ForwardedProperty(p, access, get, set, getHidingModifier(sym, p, compilation)));
		return null;
	}

	private static string? forwardedAccessibility(ISymbol member, INamedTypeSymbol sym, INamedTypeSymbol wrapped, Compilation compilation) {
		if (!compilation.IsSymbolAccessibleWithin(member, sym, throughType: wrapped))
			return null;
		return member.DeclaredAccessibility switch {
			Accessibility.Public => "public",
			Accessibility.Internal or Accessibility.ProtectedOrInternal => "internal",
			_ => null,
		};
	}

	// what the generated member needs to not trigger a hiding warning (or error, for abstract members)
	private static string getHidingModifier(INamedTypeSymbol sym, ISymbol member, Compilation compilation) {
		for (INamedTypeSymbol? type = sym.BaseType; type is not null; type = type.BaseType) {
			foreach (ISymbol baseMember in type.GetMembers(member.Name)) {
				if (baseMember.IsStatic || !compilation.IsSymbolAccessibleWithin(baseMember, sym))
					continue;
				if (baseMember.Kind == member.Kind && baseMember is IMethodSymbol bm && member is IMethodSymbol m && !sameSignature(bm, m))
					continue; // overload, not hiding
				bool overridable = baseMember.Kind == member.Kind &&
					(baseMember.IsVirtual || baseMember.IsAbstract || baseMember.IsOverride) &&
					!baseMember.IsSealed;
				return overridable ? "override " : "new ";
			}
		}
		return "";
	}

	private static bool sameSignature(IMethodSymbol a, IMethodSymbol b) {
		if (a.Arity != b.Arity || a.Parameters.Length != b.Parameters.Length)
			return false;
		for (int i = 0; i < a.Parameters.Length; i++)
			if (a.Parameters[i].RefKind != b.Parameters[i].RefKind ||
				!SymbolEqualityComparer.Default.Equals(a.Parameters[i].Type, b.Parameters[i].Type))
				return false;
		return true;
	}
}
