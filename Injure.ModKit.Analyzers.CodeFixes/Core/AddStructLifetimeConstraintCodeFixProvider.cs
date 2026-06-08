// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.ModKit.Analyzers.CodeFixes.Core;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddStructLifetimeConstraintCodeFixProvider))]
[Shared]
public sealed class AddStructLifetimeConstraintCodeFixProvider : CodeFixProvider {
	public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(
		Diagnostics.Core.MissingStructLifetimeConstraint.Id
	);

	public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
		SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root is null)
			return;
		foreach (Diagnostic diagnostic in context.Diagnostics) {
			SyntaxNode? node = root.FindNode(diagnostic.Location.SourceSpan);
			TypeParameterConstraintClauseSyntax? clause = node.FirstAncestorOrSelf<TypeParameterConstraintClauseSyntax>();
			if (clause is null || !canSafelyAddStructConstraint(clause))
				return;
			context.RegisterCodeFix(
				CodeAction.Create(
					"Add 'struct' to IModLifetimeIdentity constraint",
					ct => addStructConstraintAsync(context.Document, clause, ct),
					equivalenceKey: "AddStructLifetimeConstraint"
				),
				diagnostic
			);
		}
	}

	private static bool canSafelyAddStructConstraint(TypeParameterConstraintClauseSyntax clause) {
		foreach (TypeParameterConstraintSyntax constraint in clause.Constraints) {
			if (constraint is ConstructorConstraintSyntax)
				return false;
			if (constraint is ClassOrStructConstraintSyntax cos) {
				if (cos.ClassOrStructKeyword.IsKind(SyntaxKind.ClassKeyword))
					return false;
				if (cos.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword))
					return false;
			}
			if (constraint is TypeConstraintSyntax tc) {
				string text = tc.Type.ToString();
				if (text is "notnull" or "unmanaged")
					return false;
			}
		}
		return true;
	}

	private static async Task<Document> addStructConstraintAsync(Document doc, TypeParameterConstraintClauseSyntax clause, CancellationToken ct) {
		SyntaxNode? root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
		if (root is null)
			return doc;
		TypeParameterConstraintSyntax structConstraint = SyntaxFactory.ClassOrStructConstraint(SyntaxKind.StructConstraint).WithTrailingTrivia(SyntaxFactory.Space);
		SeparatedSyntaxList<TypeParameterConstraintSyntax> old = clause.Constraints;
		SeparatedSyntaxList<TypeParameterConstraintSyntax> @new = SyntaxFactory.SeparatedList(
			new[] { structConstraint }.Concat(old),
			Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space), old.Count)
		);
		TypeParameterConstraintClauseSyntax newClause = clause.WithConstraints(@new).WithAdditionalAnnotations(Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);
		SyntaxNode newRoot = root.ReplaceNode(clause, newClause);
		return doc.WithSyntaxRoot(newRoot);
	}
}
