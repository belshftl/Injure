// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.Documentation;

// accepted forms for the last paragraph of a struct's type-level <remarks>:
//   The <see langword="default"/> value is invalid.[ anything]
//   The <see langword="default"/> value is valid[anything other than just a period]
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StructDefaultValueDocAnalyzer : DiagnosticAnalyzer {
	private const string prefix = "The <see langword=\"default\"/> value is ";

	private static readonly Regex whitespace = new(@"\s+", RegexOptions.CultureInvariant);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
		Diagnostics.Documentation.StructDefaultValueUndocumented
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(analyze, SymbolKind.NamedType);
	}

	private static void analyze(SymbolAnalysisContext ctx) {
		var sym = (INamedTypeSymbol)ctx.Symbol;
		if (sym.TypeKind != TypeKind.Struct || sym.IsImplicitlyDeclared)
			return;
		Location loc = Util.GetPrimaryLocation(sym);
		// without doc comment parsing there is nothing to look at
		if (loc.SourceTree is not SyntaxTree tree || tree.Options.DocumentationMode == DocumentationMode.None)
			return;

		string? problem = check(sym.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: ctx.CancellationToken));
		if (problem is not null)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Documentation.StructDefaultValueUndocumented, loc, sym.Name, problem));
	}

	// returns the problem for the diagnostic message, or null if the doc comment is fine
	private static string? check(string? xml) {
		if (string.IsNullOrWhiteSpace(xml))
			return "has no doc comment";
		XElement root;
		try {
			root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
		} catch (XmlException) {
			return null; // malformed XML is already reported by the compiler
		}
		if (root.Elements("inheritdoc").Any())
			return null;
		XElement? remarks = root.Elements("remarks").LastOrDefault();
		if (remarks is null)
			return "has no <remarks>";

		string para = whitespace.Replace(render(lastParagraph(remarks)), " ").Trim();
		const string notDocumented = "does not document its default value in the last paragraph of its <remarks>";
		if (!para.StartsWith(prefix, StringComparison.Ordinal))
			return notDocumented;
		string rest = para[prefix.Length..];
		if (startsWithWord(rest, "invalid"))
			return null;
		if (!startsWithWord(rest, "valid"))
			return notDocumented;
		string explanation = rest["valid".Length..].Trim();
		return explanation.Length == 0 || explanation == "." ? "says its default value is valid but not what the default value is" : null;
	}

	// the last <para>, or the trailing run of non-<para> content after it if there is any
	private static IEnumerable<XNode> lastParagraph(XElement remarks) {
		var nodes = remarks.Nodes().ToList();
		int end = nodes.Count;
		while (end > 0 && nodes[end - 1] is XText t && string.IsNullOrWhiteSpace(t.Value))
			end--;
		if (end == 0)
			return [];
		if (isPara(nodes[end - 1]))
			return ((XElement)nodes[end - 1]).Nodes();
		int start = end;
		while (start > 0 && !isPara(nodes[start - 1]))
			start--;
		return nodes.GetRange(start, end - start);
	}

	private static bool isPara(XNode node) => node is XElement e && e.Name.LocalName == "para";

	private static string render(IEnumerable<XNode> nodes) {
		StringBuilder sb = new();
		foreach (XNode node in nodes) {
			switch (node) {
			case XText t:
				sb.Append(t.Value);
				break;
			// normalized so that e.g. `<see langword="default" />` matches too
			case XElement e when e.Name.LocalName == "see" && e.Attribute("langword") is XAttribute langword && !e.Nodes().Any():
				sb.Append("<see langword=\"").Append(langword.Value).Append("\"/>");
				break;
			case XElement e:
				sb.Append(e.ToString(SaveOptions.DisableFormatting));
				break;
			}
		}
		return sb.ToString();
	}

	private static bool startsWithWord(string s, string word) =>
		s.StartsWith(word, StringComparison.Ordinal) && (s.Length == word.Length || !char.IsLetterOrDigit(s[word.Length]));
}
