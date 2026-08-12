// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Injure.DevAnalyzers.PublicApi;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicForeignTypeAnalyzer : DiagnosticAnalyzer {
	private static readonly ImmutableHashSet<string> whitelist = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"Injure",
		"Injure.Mods.Abstractions",
		"Injure.Mods.Runtime",
		"Injure.Native"
	);

	private static readonly ImmutableHashSet<string> bclPublicKeyTokens = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"b77a5c561934e089",
		"b03f5f7f11d50a3a",
		"7cec85d7bea7798e",
		"cc7b13ffcd2ddd51"
	);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.PublicApi.PublicForeignType
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(ctx => {
			// TODO this should probably use a KnownSymbols type like everything else does but auguhag i'm tired rn
			IAssemblySymbol compilationAssembly = ctx.Compilation.Assembly;
			IAssemblySymbol coreLibrary = ctx.Compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
			ctx.RegisterSymbolAction(
				c => analyzeSymbol(c, compilationAssembly, coreLibrary),
				SymbolKind.NamedType,
				SymbolKind.Method,
				SymbolKind.Property,
				SymbolKind.Field,
				SymbolKind.Event
			);
		});
	}

	private static void analyzeSymbol(SymbolAnalysisContext ctx, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		switch (ctx.Symbol) {
		case INamedTypeSymbol type:
			analyzeNamedType(ctx, type, compilationAssembly, coreLibrary);
			break;
		case IMethodSymbol method:
			analyzeMethod(ctx, method, compilationAssembly, coreLibrary);
			break;
		case IPropertySymbol property:
			analyzeProperty(ctx, property, compilationAssembly, coreLibrary);
			break;
		case IFieldSymbol field:
			analyzeField(ctx, field, compilationAssembly, coreLibrary);
			break;
		case IEventSymbol @event:
			analyzeEvent(ctx, @event, compilationAssembly, coreLibrary);
			break;
		}
	}

	private static void analyzeNamedType(SymbolAnalysisContext ctx, INamedTypeSymbol type, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		TypeWalker? walker = createWalker(ctx, type, compilationAssembly, coreLibrary);
		if (walker is null)
			return;

		visitConstraints(type.TypeParameters, walker);
		if (type.TypeKind == TypeKind.Class && type.BaseType is not null)
			walker.Visit(type.BaseType);
		if (type.TypeKind == TypeKind.Delegate && type.DelegateInvokeMethod is IMethodSymbol invokeMethod)
			visitMethodSignature(invokeMethod, walker, exemptReturnType: false);
	}

	private static void analyzeMethod(SymbolAnalysisContext ctx, IMethodSymbol method, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		if (method.AssociatedSymbol is not null || method.MethodKind == MethodKind.DelegateInvoke || method.IsImplicitlyDeclared)
			return;
		if (method.PartialDefinitionPart is not null) // don't report on both parts
			return;
		TypeWalker? walker = createWalker(ctx, method, compilationAssembly, coreLibrary);
		if (walker is null)
			return;
		bool exemptReturnType = method.Name.StartsWith("DangerousGet", StringComparison.Ordinal);
		visitMethodSignature(method, walker, exemptReturnType);
	}

	private static void analyzeProperty(SymbolAnalysisContext ctx, IPropertySymbol property, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		if (property.IsImplicitlyDeclared)
			return;
		TypeWalker? walker = createWalker(ctx, property, compilationAssembly, coreLibrary);
		if (walker is null)
			return;
		walker.Visit(property.Type);
		walker.VisitCustomModifiers(property.TypeCustomModifiers);
		walker.VisitCustomModifiers(property.RefCustomModifiers);
		foreach (IParameterSymbol param in property.Parameters)
			visitParameter(param, walker);
	}

	private static void analyzeField(SymbolAnalysisContext ctx, IFieldSymbol field, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		if (field.IsImplicitlyDeclared)
			return;
		TypeWalker? walker = createWalker(ctx, field, compilationAssembly, coreLibrary);
		if (walker is null)
			return;
		walker.Visit(field.Type);
		walker.VisitCustomModifiers(field.CustomModifiers);
		walker.VisitCustomModifiers(field.RefCustomModifiers);
	}

	private static void analyzeEvent(SymbolAnalysisContext ctx, IEventSymbol @event, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		if (@event.IsImplicitlyDeclared)
			return;
		TypeWalker? walker = createWalker(ctx, @event, compilationAssembly, coreLibrary);
		if (walker is null)
			return;
		walker.Visit(@event.Type);
	}

	private static void visitMethodSignature(IMethodSymbol method, TypeWalker walker, bool exemptReturnType) {
		visitConstraints(method.TypeParameters, walker);
		foreach (IParameterSymbol param in method.Parameters)
			visitParameter(param, walker);
		if (!exemptReturnType && !method.ReturnsVoid && method.MethodKind is not MethodKind.Constructor and not MethodKind.StaticConstructor and not MethodKind.Destructor) {
			walker.Visit(method.ReturnType);
			walker.VisitCustomModifiers(method.ReturnTypeCustomModifiers);
			walker.VisitCustomModifiers(method.RefCustomModifiers);
		}
		foreach (INamedTypeSymbol conventionType in method.UnmanagedCallingConventionTypes)
			walker.Visit(conventionType);
	}

	private static void visitParameter(IParameterSymbol param, TypeWalker walker) {
		walker.Visit(param.Type);
		walker.VisitCustomModifiers(param.CustomModifiers);
		walker.VisitCustomModifiers(param.RefCustomModifiers);
	}

	private static void visitConstraints(ImmutableArray<ITypeParameterSymbol> typeParams, TypeWalker walker) {
		foreach (ITypeParameterSymbol param in typeParams)
			foreach (ITypeSymbol constraint in param.ConstraintTypes)
				walker.Visit(constraint);
	}

	private static TypeWalker? createWalker(SymbolAnalysisContext ctx, ISymbol owner, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		if (!owner.IsPubliclyVisible())
			return null;
		Location? loc = owner.Locations.FirstOrDefault(static l => l.IsInSource);
		if (loc is null)
			return null;
		return new TypeWalker(ctx, owner, loc, compilationAssembly, coreLibrary);
	}

	private static bool isAllowedAssembly(IAssemblySymbol? assembly, IAssemblySymbol compilationAssembly, IAssemblySymbol coreLibrary) {
		if (assembly is null)
			return true;
		if (SymbolEqualityComparer.Default.Equals(assembly, compilationAssembly))
			return true;
		if (whitelist.Contains(assembly.Identity.Name))
			return true;
		return isBclAssembly(assembly, coreLibrary);
	}

	private static bool isBclAssembly(IAssemblySymbol assembly, IAssemblySymbol coreLibrary) {
		if (SymbolEqualityComparer.Default.Equals(assembly, coreLibrary))
			return true;
		string name = assembly.Identity.Name;
		bool hasBclName =
			name == "mscorlib" ||
			name == "netstandard" ||
			name == "System" ||
			name.StartsWith("System.", StringComparison.Ordinal) ||
			name == "Microsoft.CSharp" ||
			name == "Microsoft.VisualBasic" ||
			name == "Microsoft.VisualBasic.Core" ||
			name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
		if (!hasBclName)
			return false;
		return bclPublicKeyTokens.Contains(publicKeyTokenString(assembly.Identity.PublicKeyToken));
	}

	private static string publicKeyTokenString(ImmutableArray<byte> token) {
		if (token.IsDefaultOrEmpty)
			return string.Empty;
		const string hex = "0123456789abcdef";
		char[] result = new char[token.Length * 2];
		for (int i = 0; i < token.Length; ++i) {
			result[i * 2] = hex[token[i] >> 4];
			result[i * 2 + 1] = hex[token[i] & 0xf];
		}
		return new string(result);
	}

	private sealed class TypeWalker(
		SymbolAnalysisContext context,
		ISymbol owner,
		Location location,
		IAssemblySymbol compilationAssembly,
		IAssemblySymbol coreLibrary
	) {
		private readonly SymbolAnalysisContext ctx = context;
		private readonly ISymbol owner = owner;
		private readonly Location loc = location;
		private readonly IAssemblySymbol compilationAssembly = compilationAssembly;
		private readonly IAssemblySymbol coreLibrary = coreLibrary;

		private readonly HashSet<INamedTypeSymbol> reportedTypes = new(SymbolEqualityComparer.Default);

		public void Visit(ITypeSymbol type) {
			switch (type) {
			case IArrayTypeSymbol array:
				VisitCustomModifiers(array.CustomModifiers);
				Visit(array.ElementType);
				break;
			case IPointerTypeSymbol pointer:
				VisitCustomModifiers(pointer.CustomModifiers);
				Visit(pointer.PointedAtType);
				break;
			case IFunctionPointerTypeSymbol functionPointer:
				visitMethodSignature(functionPointer.Signature, this, exemptReturnType: false);
				break;
			case INamedTypeSymbol namedType:
				visitNamedType(namedType);
				break;
			}
		}

		public void VisitCustomModifiers(ImmutableArray<CustomModifier> modifiers) {
			foreach (CustomModifier m in modifiers)
				Visit(m.Modifier);
		}

		private void visitNamedType(INamedTypeSymbol type) {
			if (type.TypeKind == TypeKind.Error)
				return;
			INamedTypeSymbol definition = type.OriginalDefinition;
			if (reportedTypes.Add(definition) && !isAllowedAssembly(definition.ContainingAssembly, compilationAssembly, coreLibrary))
				ctx.ReportDiagnostic(
					Diagnostic.Create(
						Diagnostics.PublicApi.PublicForeignType,
						loc,
						owner.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
						type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
						definition.ContainingAssembly?.Identity.Name ?? "<unknown>"
					)
				);
			if (type.ContainingType is not null)
				Visit(type.ContainingType);
			for (int i = 0; i < type.TypeArguments.Length; ++i) {
				VisitCustomModifiers(type.GetTypeArgumentCustomModifiers(i));
				Visit(type.TypeArguments[i]);
			}
		}
	}
}
