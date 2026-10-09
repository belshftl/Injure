// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.ColorType;

internal enum ColorTypeKind {
	Color32,
	ColorF128,
}

internal static class ColorTypeModel {
	public static FrozenSet<string> GetMemberNames(ColorTypeKind kind) => kind switch {
		ColorTypeKind.Color32 => Constants.ColorType.Color32MemberNames,
		ColorTypeKind.ColorF128 => Constants.ColorType.ColorF128MemberNames,
		_ => throw new ArgumentOutOfRangeException(nameof(kind)),
	};

	// returns false if any error was reported
	public static bool Validate(INamedTypeSymbol sym, AttributeData attr, ColorTypeKind kind, DiagnosticSink sink, CancellationToken ct) {
		Location loc = Util.GetAttributeLocation(attr, sym, ct);
		bool validTarget = Util.ValidateReadonlyStructTarget(
			sym,
			loc,
			allowNested: false,
			Diagnostics.ColorType.ColorTypeInvalidTarget,
			Diagnostics.ColorType.ColorTypeMustBeReadonly,
			sink,
			ct
		);
		if (!validTarget)
			return false;
		if (Util.HasAttribute(sym, Constants.ColorType.Color32AttributeMetadataName) &&
			Util.HasAttribute(sym, Constants.ColorType.ColorF128AttributeMetadataName)) {
			sink.Report(Diagnostics.ColorType.ColorTypeInvalidTarget, loc, "Target can't be both a Color32Type and a ColorF128Type.");
			return false;
		}

		FrozenSet<string> names = GetMemberNames(kind);
		bool ok = true;
		foreach (ISymbol member in sym.GetMembers()) {
			if (member.IsImplicitlyDeclared || !names.Contains(member.Name) || !isUserDeclared(member))
				continue;
			sink.Report(Diagnostics.ColorType.ColorTypeMemberCollision, Util.GetLocation(member, sym), sym.Name, member.Name);
			ok = false;
		}
		foreach (IMethodSymbol ctor in sym.InstanceConstructors) {
			if (ctor.IsImplicitlyDeclared || !isUserDeclared(ctor) || ctor.Parameters.Length != 4)
				continue;
			sink.Report(Diagnostics.ColorType.ColorTypeMemberCollision, Util.GetLocation(ctor, sym), sym.Name, ".ctor");
			ok = false;
		}
		return ok;
	}

	// the generator's own output is part of the compilation the analyzer sees
	private static bool isUserDeclared(ISymbol member) {
		foreach (Location l in member.Locations)
			if (l.IsInSource && l.SourceTree is { } tree && !tree.FilePath.EndsWith(Constants.ColorType.GeneratedSourceSuffix, StringComparison.Ordinal))
				return true;
		return false;
	}
}
