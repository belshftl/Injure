// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Text;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.DevAnalyzers.ClosedFlags;

[Generator]
public sealed class ClosedFlagsGenerator : IIncrementalGenerator {
	// ==========================================================================
	// internal types
	private sealed class TargetInfo(INamedTypeSymbol symbol, bool defaultIsInvalid, ImmutableArray<BitInfo> bits, ImmutableArray<MirrorInfo> mirrors) {
		public INamedTypeSymbol Symbol { get; } = symbol;
		public bool DefaultIsInvalid { get; } = defaultIsInvalid;
		public ImmutableArray<BitInfo> Bits { get; } = bits;
		public ImmutableArray<MirrorInfo> Mirrors { get; } = mirrors;
	}

	private readonly record struct BitInfo(string Name, ulong Value);

	// ==========================================================================
	// IIncrementalGenerator
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		SourceGen.RegisterPostInitializationSources(
			context,
			(Constants.ClosedFlags.AttributeFilename, Constants.ClosedFlags.AttributeSource),
			(Constants.ClosedFlags.MirrorAttributeFilename, Constants.ClosedFlags.MirrorAttributeSource)
		);
		IncrementalValuesProvider<TargetInfo?> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			Constants.ClosedFlags.AttributeMetadataName,
			predicate: static (node, _) => node is StructDeclarationSyntax,
			transform: check
		);
		SourceGen.RegisterTargetOutput(context, targets, static info => info.Symbol, Constants.ClosedFlags.GeneratedSourceSuffix, emit);
	}

	private static TargetInfo? check(GeneratorAttributeSyntaxContext ctx, CancellationToken ct) {
		var sym = (INamedTypeSymbol)ctx.TargetSymbol;
		ClosedTypeShape? shape = ClosedFlagsModel.Validate(sym, ctx.Attributes[0], DiagnosticSink.Silent(), ct);
		if (shape is null)
			return null;
		return new TargetInfo(
			sym,
			shape.DefaultIsInvalid,
			shape.Members.Select(static m => new BitInfo(m.Field.Name, m.Value)).ToImmutableArray(),
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

		SourceGen.AppendGeneratedCodeAttribute(sb, nameof(ClosedFlagsGenerator));
		sb.Append('\t').Append(Util.GetTypeHeader(info.Symbol)).Append(" : global::System.IEquatable<").Append(targetType).AppendLine("> {");
		sb.Append("\t\tprivate readonly Bits ").Append(Constants.ClosedFlags.BackingFieldName).AppendLine(";");

		sb.Append("\t\tprivate ").Append(targetType).AppendLine("(Bits mask) {");
		sb.Append("\t\t\t").Append(Constants.ClosedFlags.BackingFieldName).AppendLine(" = mask;");
		sb.AppendLine("\t\t}");

		foreach (BitInfo b in info.Bits) {
			string name = Util.EscapeIdentifier(b.Name);
			sb.Append("\t\t/// <inheritdoc cref=\"Bits.").Append(name).AppendLine("\"/>");
			sb.Append("\t\tpublic static ").Append(targetType).Append(' ').Append(name).Append(" => ");
			if (!info.DefaultIsInvalid && b.Value == 0)
				sb.AppendLine("default;");
			else
				sb.Append("new(Bits.").Append(name).AppendLine(");");
		}

		sb.AppendLine("\t\t/// <summary>");
		sb.Append("\t\t/// Gets the declared flags represented by this ").Append(targetType).AppendLine(" value.");
		sb.AppendLine("\t\t/// </summary>");
		sb.AppendLine("\t\t/// <exception cref=\"global::System.InvalidOperationException\">");
		sb.AppendLine("\t\t/// This value is not valid.");
		sb.AppendLine("\t\t/// </exception>");
		sb.AppendLine("\t\t/// <remarks>");
		sb.AppendLine("\t\t/// Never returns an undeclared <see cref=\"Bits\"/> value; either returns a value with its set bits");
		sb.AppendLine("\t\t/// all being declared values or throws.");
		sb.AppendLine("\t\t/// </remarks>");
		sb.Append("\t\tpublic Bits Mask => ").Append(Constants.ClosedFlags.ValidateMethodName).Append('(').Append(Constants.ClosedFlags.BackingFieldName).AppendLine(");");

		sb.Append("\t\tpublic const Bits ").Append(Constants.ClosedFlags.AllBitsConstName).Append(" = ");
		for (int i = 0; i < info.Bits.Length; i++) {
			if (i != 0)
				sb.Append(" | ");
			sb.Append("Bits.").Append(Util.EscapeIdentifier(info.Bits[i].Name));
		}
		sb.AppendLine(";");

		sb.Append(
			"\t\t[global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] private static bool "
		).Append(Constants.ClosedFlags.IsDefinedMethodName).Append("(Bits mask) => (mask & ~").Append(Constants.ClosedFlags.AllBitsConstName).AppendLine(") == 0;");
		sb.Append(
			"\t\t[global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] private static Bits "
		).Append(Constants.ClosedFlags.ValidateMethodName).AppendLine("(Bits mask) {");
		if (info.DefaultIsInvalid) {
			sb.AppendLine("\t\t\tif (mask == (Bits)0)");
			sb.Append("\t\t\t\tthrow new global::System.InvalidOperationException(")
				.Append(SymbolDisplay.FormatLiteral("default(" + info.Symbol.Name + ") is not a valid " + info.Symbol.Name + " value", true)).AppendLine(");");
		}
		sb.Append("\t\t\tif (!").Append(Constants.ClosedFlags.IsDefinedMethodName).AppendLine("(mask))");
		sb.Append("\t\t\t\tthrow new global::System.InvalidOperationException(").Append(SymbolDisplay.FormatLiteral("Invalid " + info.Symbol.Name + " value: ", true))
			.AppendLine(" + mask.ToString());");
		sb.AppendLine("\t\t\treturn mask;");
		sb.AppendLine("\t\t}");

		sb.Append("\t\tpublic bool HasAny(").Append(targetType).AppendLine(" flags) => (Mask & flags.Mask) != 0;");
		sb.Append("\t\tpublic bool HasAll(").Append(targetType).AppendLine(" flags) => (Mask & flags.Mask) == flags.Mask;");
		sb.Append("\t\tpublic bool HasNone(").Append(targetType).AppendLine(" flags) => (Mask & flags.Mask) == 0;");

		sb.Append("\t\tpublic bool Equals(").Append(targetType).Append(" other) => ").Append(Constants.ClosedFlags.BackingFieldName).Append(" == other.")
			.Append(Constants.ClosedFlags.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic override bool Equals(object? obj) => obj is ").Append(targetType).AppendLine(" other && Equals(other);");
		sb.Append("\t\tpublic override int GetHashCode() => ").Append(Constants.ClosedFlags.BackingFieldName).AppendLine(".GetHashCode();");
		sb.Append("\t\tpublic static bool operator ==(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.ClosedFlags.BackingFieldName).Append(" == right.").Append(Constants.ClosedFlags.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator !=(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.")
			.Append(Constants.ClosedFlags.BackingFieldName).Append(" != right.").Append(Constants.ClosedFlags.BackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator |(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(")
			.Append(Constants.ClosedFlags.ValidateMethodName).AppendLine("(left.Mask | right.Mask));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator &(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(")
			.Append(Constants.ClosedFlags.ValidateMethodName).AppendLine("(left.Mask & right.Mask));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator ^(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(")
			.Append(Constants.ClosedFlags.ValidateMethodName).AppendLine("(left.Mask ^ right.Mask));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator ~(").Append(targetType).Append(" val) => new(").Append(Constants.ClosedFlags.ValidateMethodName)
			.Append("((~val.Mask) & ").Append(Constants.ClosedFlags.AllBitsConstName).AppendLine("));");

		foreach ((string mirror, string access) in info.Mirrors) {
			// operators must be public, so foreign mirrors only get the internal FromMirror helpers
			if (access != "public")
				continue;
			sb.Append("\t\tpublic static explicit operator ").Append(mirror).Append('(').Append(targetType).Append(" value) => (").Append(mirror).AppendLine(")value.Mask;");
		}

		sb.AppendLine("\t\t/// <summary>");
		sb.AppendLine("\t\t/// Returns the declared mask value for this value.");
		sb.AppendLine("\t\t/// </summary>");
		sb.AppendLine("\t\tpublic override string ToString() => Mask.ToString();");

		emitFlagsHelper(sb, info, targetType);

		sb.AppendLine("\t}");
		SourceGen.CloseScopes(sb, info.Symbol);
		return sb.ToString();
	}

	private static void emitFlagsHelper(StringBuilder sb, TargetInfo info, string targetType) {
		sb.AppendLine("\t\tpublic static class Flags {");

		sb.Append("\t\t\tprivate static readonly ").Append(targetType).AppendLine("[] _values = new[] {");
		foreach (BitInfo b in info.Bits)
			sb.Append("\t\t\t\t").Append(targetType).Append('.').Append(Util.EscapeIdentifier(b.Name)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.Append("\t\t\tpublic static global::System.ReadOnlySpan<").Append(targetType).AppendLine("> Values => _values;");

		sb.AppendLine("\t\t\tprivate static readonly Bits[] _bitValues = new[] {");
		foreach (BitInfo b in info.Bits)
			sb.Append("\t\t\t\tBits.").Append(Util.EscapeIdentifier(b.Name)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.AppendLine("\t\t\tpublic static global::System.ReadOnlySpan<Bits> BitValues => _bitValues;");

		sb.AppendLine("\t\t\tprivate static readonly string[] _names = new[] {");
		foreach (BitInfo b in info.Bits)
			sb.Append("\t\t\t\t").Append(SymbolDisplay.FormatLiteral(b.Name, true)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.AppendLine("\t\t\tpublic static global::System.ReadOnlySpan<string> Names => _names;");

		sb.Append("\t\t\tpublic static bool IsDefined(Bits mask) => ").Append(Constants.ClosedFlags.IsDefinedMethodName).AppendLine("(mask);");

		sb.Append("\t\t\tpublic static bool TryFromMask(Bits mask, out ").Append(targetType).AppendLine(" val) {");
		sb.Append("\t\t\t\tif (").Append(Constants.ClosedFlags.IsDefinedMethodName).AppendLine("(mask)) {");
		sb.Append("\t\t\t\t\tval = new ").Append(targetType).AppendLine("(mask);");
		sb.AppendLine("\t\t\t\t\treturn true;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\tval = default;");
		sb.AppendLine("\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t}");

		sb.Append("\t\t\tpublic static ").Append(targetType).AppendLine(" FromMask(Bits mask) {");
		sb.Append("\t\t\t\tif (TryFromMask(mask, out ").Append(targetType).AppendLine(" value))");
		sb.AppendLine("\t\t\t\t\treturn value;");
		sb.AppendLine("\t\t\t\tthrow new global::System.ArgumentOutOfRangeException(nameof(mask), mask, null);");
		sb.AppendLine("\t\t\t}");

		foreach ((string mirror, string access) in info.Mirrors) {
			sb.Append("\t\t\t").Append(access).Append(" static bool TryFromMirror(").Append(mirror).Append(" mirror, out ").Append(targetType)
				.AppendLine(" val) => TryFromMask((Bits)mirror, out val);");
			sb.Append("\t\t\t").Append(access).Append(" static ").Append(targetType).Append(" FromMirror(").Append(mirror).AppendLine(" mirror) {");
			sb.Append("\t\t\t\tif (TryFromMask((Bits)mirror, out ").Append(targetType).AppendLine(" val))");
			sb.AppendLine("\t\t\t\t\treturn val;");
			sb.AppendLine("\t\t\t\tthrow new global::System.ArgumentOutOfRangeException(nameof(mirror), mirror, null);");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine("\t\t}");
	}
}
