// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Injure.Internals.Analyzers.Language;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OpenClassMarkerAnalyzer : DiagnosticAnalyzer {
	public const string Marker = "/* open */";

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
		Diagnostics.Language.MissingOpenClassMarker,
		Diagnostics.Language.OpenClassMarkerNotAllowed,
		Diagnostics.Language.MalformedOpenClassMarker
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(
			analyzeDeclaration,
			SyntaxKind.ClassDeclaration,
			SyntaxKind.RecordDeclaration,
			SyntaxKind.RecordStructDeclaration,
			SyntaxKind.StructDeclaration,
			SyntaxKind.InterfaceDeclaration,
			SyntaxKind.EnumDeclaration,
			SyntaxKind.DelegateDeclaration
		);
	}

	private static void analyzeDeclaration(SyntaxNodeAnalysisContext ctx) {
		SyntaxList<AttributeListSyntax> attrs;
		SyntaxTokenList modifiers;
		SyntaxToken keyword;
		int headerEnd;
		bool isClassLike;

		switch (ctx.Node) {
		case ClassDeclarationSyntax decl:
			attrs = decl.AttributeLists;
			modifiers = decl.Modifiers;
			keyword = decl.Keyword;
			headerEnd = getHeaderEnd(
				decl.OpenBraceToken,
				decl.SemicolonToken,
				decl.Span.End
			);
			isClassLike = true;
			break;
		case RecordDeclarationSyntax decl:
			attrs = decl.AttributeLists;
			modifiers = decl.Modifiers;
			keyword = decl.Keyword;
			headerEnd = getHeaderEnd(
				decl.OpenBraceToken,
				decl.SemicolonToken,
				decl.Span.End
			);
			isClassLike = decl.IsKind(SyntaxKind.RecordDeclaration);
			break;
		case StructDeclarationSyntax decl:
			attrs = decl.AttributeLists;
			modifiers = decl.Modifiers;
			keyword = decl.Keyword;
			headerEnd = getHeaderEnd(
				decl.OpenBraceToken,
				decl.SemicolonToken,
				decl.Span.End
			);
			isClassLike = false;
			break;
		case InterfaceDeclarationSyntax decl:
			attrs = decl.AttributeLists;
			modifiers = decl.Modifiers;
			keyword = decl.Keyword;
			headerEnd = getHeaderEnd(
				decl.OpenBraceToken,
				decl.SemicolonToken,
				decl.Span.End
			);
			isClassLike = false;
			break;
		case EnumDeclarationSyntax decl:
			attrs = decl.AttributeLists;
			modifiers = decl.Modifiers;
			keyword = decl.EnumKeyword;
			headerEnd = getHeaderEnd(
				decl.OpenBraceToken,
				decl.SemicolonToken,
				decl.Span.End
			);
			isClassLike = false;
			break;
		case DelegateDeclarationSyntax decl:
			attrs = decl.AttributeLists;
			modifiers = decl.Modifiers;
			keyword = decl.DelegateKeyword;
			headerEnd = decl.SemicolonToken.IsMissing ? decl.Span.End : decl.SemicolonToken.SpanStart;
			isClassLike = false;
			break;
		default:
			return;
		}

		List<SyntaxTrivia> beforeKeyword = new();
		List<SyntaxTrivia> allMarkers = new();

		// comments immediately after an attribute are usually trailing trivia
		if (attrs.Count != 0)
			addMarkers(attrs[^1].GetLastToken().TrailingTrivia, beforeKeyword);

		foreach (SyntaxToken modifier in modifiers) {
			addMarkers(modifier.LeadingTrivia, beforeKeyword);
			addMarkers(modifier.TrailingTrivia, beforeKeyword);
		}

		addMarkers(keyword.LeadingTrivia, beforeKeyword);
		allMarkers.AddRange(beforeKeyword);

		foreach (SyntaxTrivia trivia in ctx.Node.DescendantTrivia(descendIntoTrivia: false)) {
			if (!isMarker(trivia))
				continue;
			if (trivia.Span.Start < keyword.Span.End || trivia.Span.End > headerEnd)
				continue;
			allMarkers.Add(trivia);
		}

		bool hasClosingModifier = false;
		foreach (SyntaxToken modifier in modifiers)
			if (modifier.IsKind(SyntaxKind.AbstractKeyword) || modifier.IsKind(SyntaxKind.SealedKeyword) || modifier.IsKind(SyntaxKind.StaticKeyword)) {
				hasClosingModifier = true;
				break;
			}

		if (!isClassLike || hasClosingModifier) {
			foreach (SyntaxTrivia marker in allMarkers)
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Language.OpenClassMarkerNotAllowed, Location.Create(ctx.Node.SyntaxTree, marker.Span)));
			return;
		}

		if (allMarkers.Count == 0) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Language.MissingOpenClassMarker, keyword.GetLocation()));
			return;
		}

		SourceText text = ctx.Node.SyntaxTree.GetText(ctx.CancellationToken);
		bool valid = allMarkers.Count == 1 && beforeKeyword.Count == 1 && hasModifierSpacing(text, beforeKeyword[0].Span);
		if (valid)
			return;
		foreach (SyntaxTrivia marker in allMarkers)
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Language.MalformedOpenClassMarker, Location.Create(ctx.Node.SyntaxTree, marker.Span)));
	}

	private static void addMarkers(SyntaxTriviaList triviaList, List<SyntaxTrivia> markers) {
		foreach (SyntaxTrivia trivia in triviaList)
			if (isMarker(trivia))
				markers.Add(trivia);
	}

	private static bool isMarker(SyntaxTrivia trivia) => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) && trivia.ToString() == Marker;

	private static bool hasModifierSpacing(SourceText text, TextSpan span) {
		if (span.Start != 0) {
			char prev = text[span.Start - 1];
			if (!isHorizontalWs(prev) && prev != '\r' && prev != '\n')
				return false;
		}
		int pos = span.End;
		if (pos >= text.Length || !isHorizontalWs(text[pos]))
			return false;
		do {
			pos++;
		} while (pos < text.Length && isHorizontalWs(text[pos]));
		return pos < text.Length && text[pos] != '\r' && text[pos] != '\n';
	}

	private static bool isHorizontalWs(char value) => value == ' ' || value == '\t';

	private static int getHeaderEnd(SyntaxToken openBrace, SyntaxToken semicolon, int fallback) {
		if (openBrace.RawKind != 0 && !openBrace.IsMissing)
			return openBrace.SpanStart;
		if (semicolon.RawKind != 0 && !semicolon.IsMissing)
			return semicolon.SpanStart;
		return fallback;
	}
}
