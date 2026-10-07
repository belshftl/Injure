// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.DevAnalyzers.Shared;

// what differs between ClosedEnum and ClosedFlags as far as the shared validation is concerned
internal sealed class ClosedTypeSpec {
	public required string Name { get; init; }
	public required string NestedEnumName { get; init; }
	public required bool RequiresFlagsEnum { get; init; }
	public required string FlagsEnumMismatchMessage { get; init; }
	public required string MirrorAttributeMetadataName { get; init; }
	public required FrozenSet<string> ReservedMemberNames { get; init; }

	public required DiagnosticDescriptor InvalidTarget { get; init; }
	public required DiagnosticDescriptor MustBeReadonly { get; init; }
	public required DiagnosticDescriptor InvalidSourceShape { get; init; }
	public required DiagnosticDescriptor InvalidNestedEnum { get; init; }
	public required DiagnosticDescriptor AliasNotSupported { get; init; }
	public required DiagnosticDescriptor DefaultRule { get; init; }
	public required DiagnosticDescriptor SuspiciousZeroName { get; init; }
	public required DiagnosticDescriptor MirrorInvalid { get; init; }
}

internal readonly record struct ClosedTypeMember(IFieldSymbol Field, ulong Value);

internal readonly record struct ClosedTypeMirror(INamedTypeSymbol Enum, Location Location, bool Subset);

internal sealed class ClosedTypeShape(
	INamedTypeSymbol nestedEnum,
	bool defaultIsInvalid,
	ImmutableArray<ClosedTypeMember> members,
	ImmutableArray<ClosedTypeMirror> mirrors
) {
	public INamedTypeSymbol NestedEnum { get; } = nestedEnum;
	public bool DefaultIsInvalid { get; } = defaultIsInvalid;
	public ImmutableArray<ClosedTypeMember> Members { get; } = members; // in declaration order
	public ImmutableArray<ClosedTypeMirror> Mirrors { get; } = mirrors;
}

internal static class ClosedType {
	// returns null only if validation couldn't get far enough to produce a shape; a returned shape
	// can still have had errors reported, so callers run their own checks through the same sink and
	// then check sink.HasErrors
	public static ClosedTypeShape? Validate(ClosedTypeSpec spec, INamedTypeSymbol sym, AttributeData attr, DiagnosticSink sink, CancellationToken ct) {
		Location loc = Util.GetAttributeLocation(attr, sym, ct);
		if (!Util.ValidateReadonlyStructTarget(sym, loc, allowNested: false, spec.InvalidTarget, spec.MustBeReadonly, sink, ct))
			return null;

		List<EnumDeclarationSyntax> enumDecls = new();
		foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences) {
			if (sr.GetSyntax(ct) is not StructDeclarationSyntax decl)
				continue;
			foreach (MemberDeclarationSyntax member in decl.Members) {
				if (member is EnumDeclarationSyntax enumDecl && enumDecl.Identifier.ValueText == spec.NestedEnumName) {
					enumDecls.Add(enumDecl);
					continue;
				}
				sink.Report(
					spec.InvalidSourceShape,
					member.GetLocation(),
					$"{spec.Name} struct '{sym.Name}' must contain no members other than a nested enum named '{spec.NestedEnumName}'."
				);
			}
		}

		string exactlyOneMsg = $"{spec.Name} struct '{sym.Name}' must contain exactly one nested enum named '{spec.NestedEnumName}'.";
		if (enumDecls.Count != 1) {
			sink.Report(spec.InvalidSourceShape, loc, exactlyOneMsg);
			return null;
		}
		INamedTypeSymbol? enumSym = Util.GetSingleNestedEnum(sym, spec.NestedEnumName);
		if (enumSym is null) {
			sink.Report(spec.InvalidSourceShape, enumDecls[0].GetLocation(), exactlyOneMsg);
			return null;
		}
		if (Util.IsFlagsEnum(enumSym) != spec.RequiresFlagsEnum) {
			sink.Report(spec.InvalidNestedEnum, enumDecls[0].Identifier.GetLocation(), spec.FlagsEnumMismatchMessage);
			return null;
		}

		bool defaultIsInvalid = Util.GetBoolNamedArgument(attr, Constants.ClosedType.DefaultIsInvalidName, false);
		bool checkZeroNames = Util.GetBoolNamedArgument(attr, Constants.ClosedType.CheckZeroNameName, true);
		ImmutableArray<ClosedTypeMember> members = validateMembers(spec, enumSym, defaultIsInvalid, checkZeroNames, sink);
		ImmutableArray<ClosedTypeMirror> mirrors = validateMirrors(spec, sym, enumSym, sink, ct);
		return new ClosedTypeShape(enumSym, defaultIsInvalid, members, mirrors);
	}

	public static IEnumerable<ulong> GetEnumValues(INamedTypeSymbol enumSym) {
		foreach (IFieldSymbol field in enumSym.GetMembers().OfType<IFieldSymbol>())
			if (!field.IsImplicitlyDeclared && field.HasConstantValue && Util.TryGetEnumMemberUInt64(field, out ulong v))
				yield return v;
	}

	private static ImmutableArray<ClosedTypeMember> validateMembers(
		ClosedTypeSpec spec,
		INamedTypeSymbol enumSym,
		bool defaultIsInvalid,
		bool checkZeroNames,
		DiagnosticSink sink
	) {
		Dictionary<ulong, IFieldSymbol> seenValues = new();
		ImmutableArray<ClosedTypeMember>.Builder members = ImmutableArray.CreateBuilder<ClosedTypeMember>();
		IFieldSymbol? zeroField = null;
		foreach (IFieldSymbol field in enumSym.GetMembers().OfType<IFieldSymbol>()) {
			if (field.IsImplicitlyDeclared || !field.HasConstantValue)
				continue;
			Location fieldLoc = Util.GetLocation(field, enumSym);
			if (spec.ReservedMemberNames.Contains(field.Name)) {
				sink.Report(
					spec.InvalidNestedEnum,
					fieldLoc,
					$"{spec.NestedEnumName} member name '{field.Name}' is reserved by generated {spec.Name} code."
				);
				continue;
			}
			if (!Util.TryGetEnumMemberUInt64(field, out ulong v)) {
				sink.Report(spec.InvalidNestedEnum, fieldLoc, $"{spec.NestedEnumName} member '{field.Name}' does not have a supported constant value.");
				continue;
			}
			if (seenValues.TryGetValue(v, out IFieldSymbol? existing)) {
				sink.Report(spec.AliasNotSupported, fieldLoc, field.Name, existing.Name, Util.UInt64Display(v));
				continue;
			}
			seenValues.Add(v, field);
			members.Add(new ClosedTypeMember(field, v));
			if (v == 0)
				zeroField = field; // aliases are rejected above, so there's at most one
		}

		if (defaultIsInvalid) {
			if (zeroField is not null)
				sink.Report(
					spec.DefaultRule,
					Util.GetLocation(zeroField, enumSym),
					$"{spec.NestedEnumName} enum must not have any members with a value of 0 when DefaultIsInvalid = true."
				);
			else if (members.Count == 0)
				sink.Report(spec.InvalidNestedEnum, Util.GetPrimaryLocation(enumSym), $"{spec.NestedEnumName} enum must declare at least one member.");
		} else if (zeroField is null) {
			sink.Report(
				spec.DefaultRule,
				Util.GetPrimaryLocation(enumSym),
				$"{spec.NestedEnumName} enum must have exactly one member with a value of 0 when DefaultIsInvalid = false."
			);
		} else if (checkZeroNames && !isNeutralZeroName(zeroField.Name)) {
			sink.Report(spec.SuspiciousZeroName, Util.GetLocation(zeroField, enumSym), zeroField.Name);
		}
		return members.ToImmutable();
	}

	private static ImmutableArray<ClosedTypeMirror> validateMirrors(
		ClosedTypeSpec spec,
		INamedTypeSymbol sym,
		INamedTypeSymbol enumSym,
		DiagnosticSink sink,
		CancellationToken ct
	) {
		ImmutableArray<ClosedTypeMirror>.Builder mirrors = ImmutableArray.CreateBuilder<ClosedTypeMirror>();
		HashSet<INamedTypeSymbol> seenMirrors = new(SymbolEqualityComparer.Default);
		foreach (AttributeData attr in Util.GetAttributes(sym, spec.MirrorAttributeMetadataName)) {
			bool subset = Util.GetBoolNamedArgument(attr, Constants.ClosedType.MirrorSubsetName, false);
			Location loc = Util.GetAttributeLocation(attr, sym, ct);
			if (!tryGetMirrorEnum(attr, out INamedTypeSymbol? external)) {
				sink.Report(spec.MirrorInvalid, loc, $"{spec.Name}Mirror must have exactly one typeof(TEnum) argument.");
				continue;
			}
			if (external.TypeKind != TypeKind.Enum) {
				sink.Report(spec.MirrorInvalid, loc, $"{spec.Name}Mirror target '{external.ToDisplayString()}' must be an enum.");
				continue;
			}
			if (!seenMirrors.Add(external)) {
				sink.Report(spec.MirrorInvalid, loc, $"Duplicate {spec.Name}Mirror for '{external.ToDisplayString()}'.");
				continue;
			}
			if (!SymbolEqualityComparer.Default.Equals(enumSym.EnumUnderlyingType, external.EnumUnderlyingType)) {
				sink.Report(
					spec.MirrorInvalid,
					loc,
					$"{spec.Name}Mirror target '{external.ToDisplayString()}' must have the same underlying type as '{enumSym.ToDisplayString()}'."
				);
				continue;
			}
			mirrors.Add(new ClosedTypeMirror(external, loc, subset));
		}
		return mirrors.ToImmutable();
	}

	private static bool tryGetMirrorEnum(AttributeData attr, [NotNullWhen(true)] out INamedTypeSymbol? enumType) {
		enumType = null;
		if (attr.ConstructorArguments.Length != 1)
			return false;
		TypedConstant arg = attr.ConstructorArguments[0];
		if (arg.Kind != TypedConstantKind.Type || arg.Value is not INamedTypeSymbol type)
			return false;
		enumType = type;
		return true;
	}

	private static bool isNeutralZeroName(string name) {
		if (Constants.ClosedType.NeutralZeroNames.Contains(name))
			return true;
		foreach (string prefix in Constants.ClosedType.NeutralZeroPrefixes)
			if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[prefix.Length]))
				return true;
		return false;
	}
}
