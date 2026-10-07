// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.StronglyTypedInt;

internal readonly record struct StronglyTypedIntBacking(string Name, bool Signed);

internal static class StronglyTypedIntModel {
	// returns null if any error was reported
	public static StronglyTypedIntBacking? Validate(INamedTypeSymbol sym, AttributeData attr, DiagnosticSink sink, CancellationToken ct) {
		Location loc = Util.GetAttributeLocation(attr, sym, ct);
		bool validTarget = Util.ValidateReadonlyStructTarget(
			sym,
			loc,
			allowNested: true,
			Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget,
			Diagnostics.StronglyTypedInt.StronglyTypedIntMustBeReadonly,
			sink,
			ct
		);
		if (!validTarget)
			return null;

		if (attr.ConstructorArguments.Length != 1) {
			sink.Report(Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "Attribute must have exactly one typeof(...) argument.");
			return null;
		}
		if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol backingType) {
			sink.Report(Diagnostics.StronglyTypedInt.StronglyTypedIntInvalidTarget, loc, "Attribute argument must be a concrete type.");
			return null;
		}
		if (getBackingInfo(backingType) is not StronglyTypedIntBacking backing) {
			sink.Report(Diagnostics.StronglyTypedInt.StronglyTypedIntUnsupportedBacking, loc, backingType.ToDisplayString());
			return null;
		}
		if (checkCollision(sym, backingType) is (Location collisionLoc, string collisionMsg)) {
			sink.Report(Diagnostics.StronglyTypedInt.StronglyTypedIntMemberCollision, collisionLoc, collisionMsg);
			return null;
		}
		return backing;
	}

	private static StronglyTypedIntBacking? getBackingInfo(INamedTypeSymbol sym) {
		switch (sym.SpecialType) {
		case SpecialType.System_SByte:
			return new StronglyTypedIntBacking("sbyte", true);
		case SpecialType.System_Byte:
			return new StronglyTypedIntBacking("byte", false);
		case SpecialType.System_Int16:
			return new StronglyTypedIntBacking("short", true);
		case SpecialType.System_UInt16:
			return new StronglyTypedIntBacking("ushort", false);
		case SpecialType.System_Int32:
			return new StronglyTypedIntBacking("int", true);
		case SpecialType.System_UInt32:
			return new StronglyTypedIntBacking("uint", false);
		case SpecialType.System_Int64:
			return new StronglyTypedIntBacking("long", true);
		case SpecialType.System_UInt64:
			return new StronglyTypedIntBacking("ulong", false);
		case SpecialType.System_IntPtr:
			return new StronglyTypedIntBacking("nint", true);
		case SpecialType.System_UIntPtr:
			return new StronglyTypedIntBacking("nuint", false);
		}
		if (sym.ContainingNamespace.ToDisplayString() == "System")
			switch (sym.Name) {
			case "Int128":
				return new StronglyTypedIntBacking("global::System.Int128", true);
			case "UInt128":
				return new StronglyTypedIntBacking("global::System.UInt128", false);
			}
		return null;
	}

	private static (Location, string)? checkCollision(INamedTypeSymbol sym, INamedTypeSymbol backingType) {
		ImmutableArray<ISymbol> members = sym.GetMembers(Constants.StronglyTypedInt.BackingFieldName);
		if (members.Length != 0)
			return (
				Util.GetLocation(members[0], sym),
				$"Type '{sym.Name}' already contains a member named '{Constants.StronglyTypedInt.BackingFieldName}', which is reserved by StronglyTypedInt."
			);
		foreach (IMethodSymbol ctor in sym.InstanceConstructors) {
			if (ctor.IsImplicitlyDeclared || ctor.Parameters.Length != 1 ||
				!SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, backingType))
				continue;
			return (
				Util.GetLocation(ctor, sym),
				$"Type '{sym.Name}' already contains a constructor with signature '({backingType.ToDisplayString()})', which conflicts with generated code."
			);
		}
		return null;
	}
}
