// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Text;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.DevAnalyzers.ClosedEnum;

[Generator]
public sealed class ClosedEnumGenerator : IIncrementalGenerator {
	// ==========================================================================
	// internal types
	private sealed class TargetInfo(INamedTypeSymbol symbol, bool defaultIsInvalid, ImmutableArray<CaseInfo> cases, ImmutableArray<MirrorInfo> mirrors) {
		public INamedTypeSymbol Symbol { get; } = symbol;
		public bool DefaultIsInvalid { get; } = defaultIsInvalid;
		public ImmutableArray<CaseInfo> Cases { get; } = cases;
		public ImmutableArray<MirrorInfo> Mirrors { get; } = mirrors;
	}

	private readonly record struct CaseInfo(string Name, bool IsZero);

	// ==========================================================================
	// IIncrementalGenerator
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		SourceGen.RegisterPostInitializationSources(
			context,
			(Constants.ClosedEnum.AttributeFilename, Constants.ClosedEnum.AttributeSource),
			(Constants.ClosedEnum.MirrorAttributeFilename, Constants.ClosedEnum.MirrorAttributeSource)
		);
		IncrementalValuesProvider<TargetInfo?> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			Constants.ClosedEnum.AttributeMetadataName,
			predicate: static (node, _) => node is StructDeclarationSyntax,
			transform: check
		);
		SourceGen.RegisterTargetOutput(context, targets, static info => info.Symbol, Constants.ClosedEnum.GeneratedSourceSuffix, emit);
	}

	private static TargetInfo? check(GeneratorAttributeSyntaxContext ctx, CancellationToken ct) {
		var sym = (INamedTypeSymbol)ctx.TargetSymbol;
		ClosedTypeShape? shape = ClosedEnumModel.Validate(sym, ctx.Attributes[0], DiagnosticSink.Silent(), ct);
		if (shape is null)
			return null;
		return new TargetInfo(
			sym,
			shape.DefaultIsInvalid,
			shape.Members.Select(static m => new CaseInfo(m.Field.Name, m.Value == 0)).ToImmutableArray(),
			shape.Mirrors.Select(m => new MirrorInfo(
				m.Enum.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				// foreign types must stay out of public API, see IJDEV0100
				ForeignTypes.IsForeign(m.Enum, ctx.SemanticModel.Compilation) ? "internal" : "public"
			)).ToImmutableArray()
		);
	}

	// ==========================================================================
	// source emission
	private static string emit(TargetInfo info) {
		string targetType = Util.EscapeIdentifier(info.Symbol.Name);
		StringBuilder sb = new(2048);

		SourceGen.AppendFileHeader(sb);
		sb.AppendLine("using System;");
		SourceGen.OpenScopes(sb, info.Symbol);

		SourceGen.AppendGeneratedCodeAttribute(sb, nameof(ClosedEnumGenerator));
		sb.Append('\t').Append(Util.GetTypeHeader(info.Symbol)).Append(" : global::System.IEquatable<").Append(targetType).AppendLine("> {");
		sb.Append("\t\tprivate readonly Case ").Append(Constants.ClosedEnum.BackingFieldName).AppendLine(";");

		sb.Append("\t\tprivate ").Append(targetType).AppendLine("(Case tag) {");
		sb.Append("\t\t\t").Append(Constants.ClosedEnum.BackingFieldName).AppendLine(" = tag;");
		sb.AppendLine("\t\t}");

		foreach (CaseInfo c in info.Cases) {
			string name = Util.EscapeIdentifier(c.Name);
			sb.Append("\t\t/// <inheritdoc cref=\"Case.").Append(name).AppendLine("\"/>");
			sb.Append("\t\tpublic static ").Append(targetType).Append(' ').Append(name).Append(" => ");
			if (!info.DefaultIsInvalid && c.IsZero)
				sb.AppendLine("default;");
			else
				sb.Append("new(Case.").Append(name).AppendLine(");");
		}

		sb.AppendLine("\t\t/// <summary>");
		sb.Append("\t\t/// Gets the declared case represented by this ").Append(targetType).AppendLine(" value.");
		sb.AppendLine("\t\t/// </summary>");
		sb.AppendLine("\t\t/// <exception cref=\"global::System.InvalidOperationException\">");
		sb.AppendLine("\t\t/// This value is not valid.");
		sb.AppendLine("\t\t/// </exception>");
		sb.AppendLine("\t\t/// <remarks>");
		sb.AppendLine("\t\t/// Never returns an undeclared <see cref=\"Case\"/> value; either returns a declared case or throws.");
		sb.AppendLine("\t\t/// As such, the fallback/default arm of an exhaustive switch is unreachable control flow.");
		sb.AppendLine("\t\t/// </remarks>");
		sb.AppendLine("\t\tpublic Case Tag {");
		sb.AppendLine("\t\t\tget {");
		if (info.DefaultIsInvalid) {
			sb.Append("\t\t\t\tif (").Append(Constants.ClosedEnum.BackingFieldName).AppendLine(" == (Case)0)");
			sb.Append("\t\t\t\t\tthrow new global::System.InvalidOperationException(")
				.Append(SymbolDisplay.FormatLiteral("default(" + info.Symbol.Name + ") is not a valid " + info.Symbol.Name + " value", true)).AppendLine(");");
		}
		sb.Append("\t\t\t\tif (!").Append(Constants.ClosedEnum.IsDefinedMethodName).Append('(').Append(Constants.ClosedEnum.BackingFieldName).AppendLine("))");
		sb.Append("\t\t\t\t\tthrow new global::System.InvalidOperationException(").Append(SymbolDisplay.FormatLiteral("Invalid " + info.Symbol.Name + " value: ", true)).Append(" + ")
			.Append(Constants.ClosedEnum.BackingFieldName).AppendLine(".ToString());");
		sb.Append("\t\t\t\treturn ").Append(Constants.ClosedEnum.BackingFieldName).AppendLine(";");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");

		sb.Append(
			"\t\t[global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] private static bool "
		).Append(Constants.ClosedEnum.IsDefinedMethodName).Append("(Case tag) => tag is ");
		for (int i = 0; i < info.Cases.Length; i++) {
			if (i != 0)
				sb.Append(" or ");
			sb.Append("Case.").Append(Util.EscapeIdentifier(info.Cases[i].Name));
		}
		sb.AppendLine(";");

		sb.Append("\t\tpublic bool Equals(").Append(targetType).Append(" other) => ").Append(Constants.ClosedEnum.BackingFieldName).Append(" == other.")
			.Append(Constants.ClosedEnum.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic override bool Equals(object? obj) => obj is ").Append(targetType).AppendLine(" other && Equals(other);");
		sb.Append("\t\tpublic override int GetHashCode() => ").Append(Constants.ClosedEnum.BackingFieldName).AppendLine(".GetHashCode();");
		sb.Append("\t\tpublic static bool operator ==(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.ClosedEnum.BackingFieldName).Append(" == right.").Append(Constants.ClosedEnum.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator !=(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.ClosedEnum.BackingFieldName).Append(" != right.").Append(Constants.ClosedEnum.BackingFieldName).AppendLine(";");

		foreach ((string mirror, string access) in info.Mirrors) {
			// operators must be public, so foreign mirrors only get the internal FromMirror helpers
			if (access != "public")
				continue;
			sb.Append("\t\tpublic static explicit operator ").Append(mirror).Append('(').Append(targetType).Append(" value) => (").Append(mirror).AppendLine(")value.Tag;");
		}

		sb.AppendLine("\t\t/// <summary>");
		sb.AppendLine("\t\t/// Returns the declared case name for this value.");
		sb.AppendLine("\t\t/// </summary>");
		sb.AppendLine("\t\tpublic override string ToString() => Tag.ToString();");

		emitEnumHelper(sb, info, targetType);

		sb.AppendLine("\t}");
		SourceGen.CloseScopes(sb, info.Symbol);
		return sb.ToString();
	}

	private static void emitEnumHelper(StringBuilder sb, TargetInfo info, string targetType) {
		sb.AppendLine("\t\tpublic static class Enum {");

		sb.Append("\t\t\tprivate static readonly ").Append(targetType).AppendLine("[] _values = new[] {");
		foreach (CaseInfo c in info.Cases)
			sb.Append("\t\t\t\t").Append(targetType).Append('.').Append(Util.EscapeIdentifier(c.Name)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.Append("\t\t\tpublic static global::System.ReadOnlySpan<").Append(targetType).AppendLine("> Values => _values;");

		sb.AppendLine("\t\t\tprivate static readonly Case[] _tags = new[] {");
		foreach (CaseInfo c in info.Cases)
			sb.Append("\t\t\t\tCase.").Append(Util.EscapeIdentifier(c.Name)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.AppendLine("\t\t\tpublic static global::System.ReadOnlySpan<Case> Tags => _tags;");

		sb.AppendLine("\t\t\tprivate static readonly string[] _names = new[] {");
		foreach (CaseInfo c in info.Cases)
			sb.Append("\t\t\t\t").Append(SymbolDisplay.FormatLiteral(c.Name, true)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.AppendLine("\t\t\tpublic static global::System.ReadOnlySpan<string> Names => _names;");

		sb.Append("\t\t\tpublic static bool IsDefined(Case tag) => ").Append(Constants.ClosedEnum.IsDefinedMethodName).AppendLine("(tag);");

		sb.Append("\t\t\tpublic static bool TryFromTag(Case tag, out ").Append(targetType).AppendLine(" val) {");
		sb.Append("\t\t\t\tif (").Append(Constants.ClosedEnum.IsDefinedMethodName).AppendLine("(tag)) {");
		sb.Append("\t\t\t\t\tval = new ").Append(targetType).AppendLine("(tag);");
		sb.AppendLine("\t\t\t\t\treturn true;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\tval = default;");
		sb.AppendLine("\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t}");

		sb.Append("\t\t\tpublic static ").Append(targetType).AppendLine(" FromTag(Case tag) {");
		sb.Append("\t\t\t\tif (TryFromTag(tag, out ").Append(targetType).AppendLine(" val))");
		sb.AppendLine("\t\t\t\t\treturn val;");
		sb.AppendLine("\t\t\t\tthrow new global::System.ArgumentOutOfRangeException(nameof(tag), tag, null);");
		sb.AppendLine("\t\t\t}");

		foreach ((string mirror, string access) in info.Mirrors) {
			sb.Append("\t\t\t").Append(access).Append(" static bool TryFromMirror(").Append(mirror).Append(" mirror, out ").Append(targetType)
				.AppendLine(" val) => TryFromTag((Case)mirror, out val);");
			sb.Append("\t\t\t").Append(access).Append(" static ").Append(targetType).Append(" FromMirror(").Append(mirror).AppendLine(" mirror) {");
			sb.Append("\t\t\t\tif (TryFromTag((Case)mirror, out ").Append(targetType).AppendLine(" val))");
			sb.AppendLine("\t\t\t\t\treturn val;");
			sb.AppendLine("\t\t\t\tthrow new global::System.ArgumentOutOfRangeException(nameof(mirror), mirror, null);");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine("\t\t}");
	}
}
