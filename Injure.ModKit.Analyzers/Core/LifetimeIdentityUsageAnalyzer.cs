// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.ModKit.Analyzers.Core;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LifetimeIdentityAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
		Diagnostics.Core.LifetimeIdentityInterfaceAsValue,
		Diagnostics.Core.LifetimeIdentityTypeParamAsValue,
		Diagnostics.Core.MissingStructLifetimeConstraint
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static ctx => {
				KnownTypes known = new(ctx.Compilation);
				ctx.RegisterSyntaxNodeAction(c => analyzeTypeParameterConstraintClause(c, known), SyntaxKind.TypeParameterConstraintClause);
				ctx.RegisterSyntaxNodeAction(c => analyzeVariableDeclaration(c, known), SyntaxKind.VariableDeclaration);
				ctx.RegisterSyntaxNodeAction(c => analyzeParameter(c, known), SyntaxKind.Parameter);
				ctx.RegisterSyntaxNodeAction(c => analyzeProperty(c, known), SyntaxKind.PropertyDeclaration);
				ctx.RegisterSyntaxNodeAction(c => analyzeIndexer(c, known), SyntaxKind.IndexerDeclaration);
				ctx.RegisterSyntaxNodeAction(c => analyzeMethod(c, known), SyntaxKind.MethodDeclaration);
				ctx.RegisterSyntaxNodeAction(c => analyzeDelegate(c, known), SyntaxKind.DelegateDeclaration);
				ctx.RegisterSyntaxNodeAction(c => analyzeLocalFunction(c, known), SyntaxKind.LocalFunctionStatement);
				ctx.RegisterSyntaxNodeAction(c => analyzeConversionOrTypeTest(c, known), SyntaxKind.CastExpression);
				ctx.RegisterSyntaxNodeAction(c => analyzeConversionOrTypeTest(c, known), SyntaxKind.AsExpression);
				ctx.RegisterSyntaxNodeAction(c => analyzeConversionOrTypeTest(c, known), SyntaxKind.IsExpression);
				ctx.RegisterSyntaxNodeAction(c => analyzeTypeof(c, known), SyntaxKind.TypeOfExpression);
			}
		);
	}

	private static void analyzeTypeParameterConstraintClause(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var clause = (TypeParameterConstraintClauseSyntax)ctx.Node;
		ITypeParameterSymbol? tp = findConstrainedTypeParameter(ctx.SemanticModel, clause, ctx.CancellationToken);
		if (tp is null || tp.HasValueTypeConstraint || tp.HasUnmanagedTypeConstraint || !tp.ConstraintTypes.Any(t => isLifetimeIdentityInterface(known, t)))
			return;
		ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.MissingStructLifetimeConstraint, clause.Name.GetLocation(), tp.Name));
	}

	private static ITypeParameterSymbol? findConstrainedTypeParameter(SemanticModel model, TypeParameterConstraintClauseSyntax clause, CancellationToken ct) {
		TypeParameterListSyntax? typeParameters = clause.Parent switch {
			TypeDeclarationSyntax typeDecl => typeDecl.TypeParameterList,
			DelegateDeclarationSyntax delegateDecl => delegateDecl.TypeParameterList,
			MethodDeclarationSyntax methodDecl => methodDecl.TypeParameterList,
			LocalFunctionStatementSyntax localFunction => localFunction.TypeParameterList,
			_ => null,
		};
		if (typeParameters is null)
			return null;
		foreach (TypeParameterSyntax typeParameter in typeParameters.Parameters)
			if (typeParameter.Identifier.ValueText == clause.Name.Identifier.ValueText)
				return model.GetDeclaredSymbol(typeParameter, ct);
		return null;
	}

	private static bool isLifetimeIdentityInterface(KnownTypes known, [NotNullWhen(true)] ITypeSymbol? type) => type is not null && (
		SymbolEqualityComparer.Default.Equals(known.ModLifetimeIdentityInterface, type) ||
		type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, known.ModLifetimeIdentityInterface))
	);

	private static void analyzeVariableDeclaration(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var decl = (VariableDeclarationSyntax)ctx.Node;
		reportOrdinaryTypeUse(ctx, known, decl.Type);
	}

	private static void analyzeParameter(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var param = (ParameterSyntax)ctx.Node;
		if (param.Type is not null)
			reportOrdinaryTypeUse(ctx, known, param.Type);
	}

	private static void analyzeProperty(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var prop = (PropertyDeclarationSyntax)ctx.Node;
		reportOrdinaryTypeUse(ctx, known, prop.Type);
	}

	private static void analyzeIndexer(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var idxr = (IndexerDeclarationSyntax)ctx.Node;
		reportOrdinaryTypeUse(ctx, known, idxr.Type);
	}

	private static void analyzeMethod(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var m = (MethodDeclarationSyntax)ctx.Node;
		reportOrdinaryTypeUse(ctx, known, m.ReturnType);
	}

	private static void analyzeDelegate(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var del = (DelegateDeclarationSyntax)ctx.Node;
		reportOrdinaryTypeUse(ctx, known, del.ReturnType);
	}

	private static void analyzeLocalFunction(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var lfn = (LocalFunctionStatementSyntax)ctx.Node;
		reportOrdinaryTypeUse(ctx, known, lfn.ReturnType);
	}

	private static void analyzeConversionOrTypeTest(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		TypeSyntax? type = ctx.Node switch {
			CastExpressionSyntax cast => cast.Type,
			BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.AsExpression) => bin.Right as TypeSyntax,
			BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.IsExpression) => bin.Right as TypeSyntax,
			_ => null,
		};
		if (type is not null)
			reportOrdinaryTypeUse(ctx, known, type);
	}

	private static void analyzeTypeof(SyntaxNodeAnalysisContext ctx, KnownTypes known) {
		var @typeof = (TypeOfExpressionSyntax)ctx.Node;
		reportOrdinaryTypeUse(ctx, known, @typeof.Type);
	}

	private static void reportOrdinaryTypeUse(SyntaxNodeAnalysisContext ctx, KnownTypes known, TypeSyntax typeSyntax) {
		if (typeSyntax.FirstAncestorOrSelf<TypeParameterConstraintClauseSyntax>() is not null)
			return;
		if (typeSyntax.FirstAncestorOrSelf<BaseListSyntax>() is not null)
			return;
		ITypeSymbol? type = ctx.SemanticModel.GetTypeInfo(typeSyntax).Type;
		if (isLifetimeIdentityInterface(known, type))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.LifetimeIdentityInterfaceAsValue, typeSyntax.GetLocation(), type.ToDisplayString()));
		else if (type is ITypeParameterSymbol tp && tp.ConstraintTypes.Any(t => isLifetimeIdentityInterface(known, t)))
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Core.LifetimeIdentityTypeParamAsValue, typeSyntax.GetLocation(), type.Name));
	}
}
