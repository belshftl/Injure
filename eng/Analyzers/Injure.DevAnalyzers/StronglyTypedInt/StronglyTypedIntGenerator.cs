// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Text;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.DevAnalyzers.StronglyTypedInt;

[Generator]
public sealed class StronglyTypedIntGenerator : IIncrementalGenerator {
	// ==========================================================================
	// internal types
	private sealed class TargetInfo(INamedTypeSymbol symbol, StronglyTypedIntBacking backing) {
		public INamedTypeSymbol Symbol { get; } = symbol;
		public StronglyTypedIntBacking Backing { get; } = backing;
	}

	// ==========================================================================
	// IIncrementalGenerator
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		SourceGen.RegisterPostInitializationSources(
			context,
			(Constants.StronglyTypedInt.AttributeFilename, Constants.StronglyTypedInt.AttributeSource)
		);
		IncrementalValuesProvider<TargetInfo?> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			Constants.StronglyTypedInt.AttributeMetadataName,
			predicate: static (node, _) => node is StructDeclarationSyntax,
			transform: check
		);
		SourceGen.RegisterTargetOutput(context, targets, static info => info.Symbol, Constants.StronglyTypedInt.GeneratedSourceSuffix, emit);
	}

	private static TargetInfo? check(GeneratorAttributeSyntaxContext ctx, CancellationToken ct) {
		var sym = (INamedTypeSymbol)ctx.TargetSymbol;
		return StronglyTypedIntModel.Validate(sym, ctx.Attributes[0], DiagnosticSink.Silent(), ct) is StronglyTypedIntBacking backing
			? new TargetInfo(sym, backing)
			: null;
	}

	// ==========================================================================
	// source emission
	private static string emit(TargetInfo info) {
		string targetType = Util.EscapeIdentifier(info.Symbol.Name);
		string backingType = info.Backing.Name;
		string one = $"({backingType})1";
		StringBuilder sb = new();

		SourceGen.AppendFileHeader(sb);
		sb.AppendLine("using System;");
		SourceGen.OpenScopes(sb, info.Symbol);

		SourceGen.AppendGeneratedCodeAttribute(sb, nameof(StronglyTypedIntGenerator));
		sb.Append('\t').AppendLine("[global::System.Runtime.InteropServices.StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
		sb.Append('\t').Append(Util.GetTypeHeader(info.Symbol)).Append(" : global::System.IEquatable<").Append(targetType)
			.Append(">, global::System.IComparable<").Append(targetType).AppendLine(">,");
		sb.Append("\tglobal::System.IComparable, global::System.ISpanFormattable, global::System.IParsable<").Append(targetType).AppendLine(">,");
		sb.Append("\tglobal::System.ISpanParsable<").Append(targetType).AppendLine("> {");
		sb.Append("\t\tprivate readonly ").Append(backingType).Append(' ').Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic ").Append(backingType).Append(" Value => ").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static readonly ").Append(targetType).AppendLine(" Zero = new(0);");
		sb.Append("\t\tpublic ").Append(targetType).Append("(").Append(backingType).AppendLine(" val) {");
		sb.Append("\t\t\t").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(" = val;");
		sb.AppendLine("\t\t}");

		sb.Append("\t\tpublic static explicit operator ").Append(targetType).Append("(").Append(backingType).AppendLine(" val) => new(val);");
		sb.Append("\t\tpublic static explicit operator checked ").Append(targetType).Append("(").Append(backingType).AppendLine(" val) => new(checked(val));");
		sb.Append("\t\tpublic static explicit operator ").Append(backingType).Append("(").Append(targetType).Append(" val) => val.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static explicit operator checked ").Append(backingType).Append("(").Append(targetType).Append(" val) => checked(val.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");

		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator +(").Append(targetType).Append(" val) => new(+val.").Append(Constants.StronglyTypedInt.BackingFieldName)
			.AppendLine(");");
		if (info.Backing.Signed) {
			sb.Append("\t\tpublic static ").Append(targetType).Append(" operator -(").Append(targetType).Append(" val) => new(-val.")
				.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
			sb.Append("\t\tpublic static ").Append(targetType).Append(" operator checked -(").Append(targetType).Append(" val) => new(checked(-val.")
				.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine("));");
		}

		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator ++(").Append(targetType).Append(" val) => new(val.").Append(Constants.StronglyTypedInt.BackingFieldName)
			.Append(" + ").Append(one).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator checked ++(").Append(targetType).Append(" val) => new(checked(val.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" + ").Append(one).AppendLine("));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator --(").Append(targetType).Append(" val) => new(val.").Append(Constants.StronglyTypedInt.BackingFieldName)
			.Append(" - ").Append(one).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator checked --(").Append(targetType).Append(" val) => new(checked(val.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" - ").Append(one).AppendLine("));");

		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator +(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" + right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator checked +(").Append(targetType).Append(" left, ").Append(targetType)
			.Append(" right) => new(checked(left.").Append(Constants.StronglyTypedInt.BackingFieldName).Append(" + right.").Append(Constants.StronglyTypedInt.BackingFieldName)
			.AppendLine("));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator -(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" - right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator checked -(").Append(targetType).Append(" left, ").Append(targetType)
			.Append(" right) => new(checked(left.").Append(Constants.StronglyTypedInt.BackingFieldName).Append(" - right.").Append(Constants.StronglyTypedInt.BackingFieldName)
			.AppendLine("));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator *(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" * right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator checked *(").Append(targetType).Append(" left, ").Append(targetType)
			.Append(" right) => new(checked(left.").Append(Constants.StronglyTypedInt.BackingFieldName).Append(" * right.").Append(Constants.StronglyTypedInt.BackingFieldName)
			.AppendLine("));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator /(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" / right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator checked /(").Append(targetType).Append(" left, ").Append(targetType)
			.Append(" right) => new(checked(left.").Append(Constants.StronglyTypedInt.BackingFieldName).Append(" / right.").Append(Constants.StronglyTypedInt.BackingFieldName)
			.AppendLine("));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator %(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" % right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");

		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator &(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" & right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator |(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" | right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator ^(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" ^ right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator ~(").Append(targetType).Append(" val) => new(~val.").Append(Constants.StronglyTypedInt.BackingFieldName)
			.AppendLine(");");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator <<(").Append(targetType).Append(" val, int n) => new(val.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(" << n);");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator >>(").Append(targetType).Append(" val, int n) => new(val.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(" >> n);");

		sb.Append("\t\tpublic static bool operator ==(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" == right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator !=(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" != right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator <(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" < right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator >(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" > right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator <=(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" <= right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator >=(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).Append(" >= right.").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");

		sb.Append("\t\tpublic bool Equals(").Append(targetType).Append(" other) => ").Append(Constants.StronglyTypedInt.BackingFieldName).Append(" == other.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic override bool Equals(object? obj) => obj is ").Append(targetType).AppendLine(" other && Equals(other);");
		sb.Append("\t\tpublic override int GetHashCode() => ").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(".GetHashCode();");

		sb.Append("\t\tpublic int CompareTo(").Append(targetType).Append(" other) => ").Append(Constants.StronglyTypedInt.BackingFieldName).Append(".CompareTo(other.")
			.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(");");
		sb.Append("\t\tpublic int CompareTo(object? obj) => obj is ").Append(targetType).AppendLine(" other ? CompareTo(other) : 1;");

		sb.Append("\t\tpublic override string ToString() => ").Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(".ToString();");
		sb.Append("\t\tpublic string ToString(string? format, IFormatProvider? provider) => ").Append(Constants.StronglyTypedInt.BackingFieldName)
			.AppendLine(".ToString(format, provider);");
		sb.Append("\t\tpublic bool TryFormat(Span<char> dst, out int written, ReadOnlySpan<char> format, IFormatProvider? provider) => ")
			.Append(Constants.StronglyTypedInt.BackingFieldName).AppendLine(".TryFormat(dst, out written, format, provider);");

		sb.Append("\t\tpublic static ").Append(targetType).Append(" Parse(string s, IFormatProvider? provider) => new(").Append(backingType).AppendLine(".Parse(s, provider));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" Parse(ReadOnlySpan<char> span, IFormatProvider? provider) => new(").Append(backingType)
			.AppendLine(".Parse(span, provider));");
		sb.Append("\t\tpublic static bool TryParse(string? s, IFormatProvider? provider, out ").Append(targetType).Append(" result) { bool ok = ").Append(backingType)
			.Append(".TryParse(s, provider, out ").Append(backingType).AppendLine(" v); result = new(v); return ok; }");
		sb.Append("\t\tpublic static bool TryParse(ReadOnlySpan<char> span, IFormatProvider? provider, out ").Append(targetType).Append(" result) { bool ok = ").Append(backingType)
			.Append(".TryParse(span, provider, out ").Append(backingType).AppendLine(" v); result = new(v); return ok; }");

		sb.AppendLine("\t}");
		SourceGen.CloseScopes(sb, info.Symbol);
		return sb.ToString();
	}
}
