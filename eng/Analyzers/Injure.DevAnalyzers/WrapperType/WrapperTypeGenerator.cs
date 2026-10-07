// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Injure.DevAnalyzers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.DevAnalyzers.WrapperType;

[Generator]
public sealed class WrapperTypeGenerator : IIncrementalGenerator {
	private static readonly SymbolDisplayFormat typeFormat = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
		SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
	);

	private static readonly FrozenSet<string> forwardedAttributes = new HashSet<string>(StringComparer.Ordinal) {
		"System.ObsoleteAttribute",
		"System.Diagnostics.CodeAnalysis.AllowNullAttribute",
		"System.Diagnostics.CodeAnalysis.ConstantExpectedAttribute",
		"System.Diagnostics.CodeAnalysis.DisallowNullAttribute",
		"System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute",
		"System.Diagnostics.CodeAnalysis.DoesNotReturnIfAttribute",
		"System.Diagnostics.CodeAnalysis.ExperimentalAttribute",
		"System.Diagnostics.CodeAnalysis.MaybeNullAttribute",
		"System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute",
		"System.Diagnostics.CodeAnalysis.NotNullAttribute",
		"System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
		"System.Diagnostics.CodeAnalysis.NotNullWhenAttribute",
		"System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute",
		"System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute",
		"System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute",
		"System.Diagnostics.CodeAnalysis.StringSyntaxAttribute",
		"System.Runtime.CompilerServices.CallerArgumentExpressionAttribute",
		"System.Runtime.CompilerServices.CallerFilePathAttribute",
		"System.Runtime.CompilerServices.CallerLineNumberAttribute",
		"System.Runtime.CompilerServices.CallerMemberNameAttribute",
	}.ToFrozenSet(StringComparer.Ordinal);

	// ==========================================================================
	// IIncrementalGenerator
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		SourceGen.RegisterPostInitializationSources(
			context,
			(Constants.WrapperType.AttributeFilename, Constants.WrapperType.AttributeSource)
		);
		IncrementalValuesProvider<WrapperTypeModel?> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			Constants.WrapperType.AttributeMetadataName,
			predicate: static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax,
			transform: static (ctx, ct) => WrapperTypeModel.Validate(
				(INamedTypeSymbol)ctx.TargetSymbol,
				ctx.Attributes[0],
				ctx.SemanticModel.Compilation,
				DiagnosticSink.Silent(),
				ct
			)
		);
		SourceGen.RegisterTargetOutput(context, targets, static model => model.Target, Constants.WrapperType.GeneratedSourceSuffix, emit);
	}

	// ==========================================================================
	// source emission
	private static string emit(WrapperTypeModel model) {
		string targetType = Util.EscapeIdentifier(model.Target.Name);
		string wrappedType = model.Wrapped.ToDisplayString(typeFormat);
		string receiver = model.NeedsCheckedAccessor ? Constants.WrapperType.CheckedAccessorName : Constants.WrapperType.BackingFieldName;
		StringBuilder sb = new(2048);

		SourceGen.AppendFileHeader(sb);
		SourceGen.OpenScopes(sb, model.Target);

		SourceGen.AppendGeneratedCodeAttribute(sb, nameof(WrapperTypeGenerator));
		sb.Append('\t').Append(Util.GetTypeHeader(model.Target)).AppendLine(" {");
		sb.Append("\t\tprivate readonly ").Append(wrappedType).Append(' ').Append(Constants.WrapperType.BackingFieldName).AppendLine(";");
		if (model.NeedsCheckedAccessor) {
			string msg = SymbolDisplay.FormatLiteral("this " + model.Target.Name + " value is uninitialized/invalid", true);
			sb.Append("\t\tprivate ").Append(wrappedType).Append(' ').Append(Constants.WrapperType.CheckedAccessorName).Append(" => ")
				.Append(Constants.WrapperType.BackingFieldName).Append(" ?? throw new global::System.InvalidOperationException(").Append(msg).AppendLine(");");
		}

		sb.Append("\t\tinternal ").Append(targetType).Append('(').Append(wrappedType).AppendLine(" inner) {");
		if (model.Wrapped.IsReferenceType)
			sb.AppendLine("\t\t\tglobal::System.ArgumentNullException.ThrowIfNull(inner);");
		sb.Append("\t\t\t").Append(Constants.WrapperType.BackingFieldName).AppendLine(" = inner;");
		sb.AppendLine("\t\t}");

		foreach (ForwardedMember member in model.Members) {
			switch (member) {
			case ForwardedMethod m:
				emitMethod(sb, m, receiver);
				break;
			case ForwardedProperty p:
				emitProperty(sb, p, receiver);
				break;
			}
		}

		sb.AppendLine("\t}");
		SourceGen.CloseScopes(sb, model.Target);
		return sb.ToString();
	}

	private static void emitMethod(StringBuilder sb, ForwardedMethod fwd, string receiver) {
		IMethodSymbol m = fwd.Method;
		string name = Util.EscapeIdentifier(m.Name);
		appendInheritdoc(sb, m);
		appendAttributeLines(sb, m.GetAttributes(), null);
		appendAttributeLines(sb, m.GetReturnTypeAttributes(), "return: ");

		sb.Append("\t\t").Append(fwd.Accessibility).Append(' ');
		if (needsUnsafe(m))
			sb.Append("unsafe ");
		sb.Append(fwd.Modifier);
		string refPrefix = m.ReturnsByRef ? "ref " : m.ReturnsByRefReadonly ? "ref readonly " : "";
		sb.Append(refPrefix).Append(m.ReturnsVoid ? "void" : m.ReturnType.ToDisplayString(typeFormat)).Append(' ').Append(name);

		string typeParams = m.TypeParameters.Length == 0 ? "" : "<" + string.Join(", ", m.TypeParameters.Select(static tp => Util.EscapeIdentifier(tp.Name))) + ">";
		sb.Append(typeParams).Append('(');
		for (int i = 0; i < m.Parameters.Length; i++) {
			if (i != 0)
				sb.Append(", ");
			appendParameter(sb, m.Parameters[i], i);
		}
		sb.Append(')');
		// overrides inherit their constraints and can't restate them
		if (fwd.Modifier != "override ")
			foreach (ITypeParameterSymbol tp in m.TypeParameters)
				appendConstraints(sb, tp);

		sb.Append(" => ").Append(refPrefix.Length != 0 ? "ref " : "").Append(receiver).Append('.').Append(name).Append(typeParams).Append('(');
		for (int i = 0; i < m.Parameters.Length; i++) {
			if (i != 0)
				sb.Append(", ");
			IParameterSymbol p = m.Parameters[i];
			sb.Append(p.RefKind switch {
				RefKind.Ref => "ref ",
				RefKind.Out => "out ",
				RefKind.In or RefKind.RefReadOnlyParameter => "in ",
				_ => "",
			}).Append(parameterName(p, i));
		}
		sb.AppendLine(");");
	}

	private static void emitProperty(StringBuilder sb, ForwardedProperty fwd, string receiver) {
		IPropertySymbol p = fwd.Property;
		string name = Util.EscapeIdentifier(p.Name);
		appendInheritdoc(sb, p);
		appendAttributeLines(sb, p.GetAttributes(), null);

		sb.Append("\t\t").Append(fwd.Accessibility).Append(' ');
		if (needsUnsafe(p.Type))
			sb.Append("unsafe ");
		sb.Append(fwd.Modifier);
		if (p.ReturnsByRef || p.ReturnsByRefReadonly) {
			sb.Append(p.ReturnsByRef ? "ref " : "ref readonly ").Append(p.Type.ToDisplayString(typeFormat)).Append(' ').Append(name)
				.Append(" => ref ").Append(receiver).Append('.').Append(name).AppendLine(";");
			return;
		}
		sb.Append(p.Type.ToDisplayString(typeFormat)).Append(' ').Append(name);
		if (fwd.SetAccessibility is null) {
			sb.Append(" => ").Append(receiver).Append('.').Append(name).AppendLine(";");
			return;
		}
		sb.AppendLine(" {");
		if (fwd.GetAccessibility is not null)
			sb.Append("\t\t\t").Append(fwd.GetAccessibility != fwd.Accessibility ? fwd.GetAccessibility + " " : "")
				.Append("get => ").Append(receiver).Append('.').Append(name).AppendLine(";");
		sb.Append("\t\t\t").Append(fwd.SetAccessibility != fwd.Accessibility ? fwd.SetAccessibility + " " : "")
			.Append("set => ").Append(receiver).Append('.').Append(name).AppendLine(" = value;");
		sb.AppendLine("\t\t}");
	}

	private static void appendInheritdoc(StringBuilder sb, ISymbol member) {
		// a documentation ID string cref doesn't get bound by the compiler, which sidesteps having to
		// spell out cref syntax for constructed generics, ref parameters and such
		string? id = member.OriginalDefinition.GetDocumentationCommentId();
		if (id is not null)
			sb.Append("\t\t/// <inheritdoc cref=\"").Append(id).AppendLine("\"/>");
	}

	private static void appendParameter(StringBuilder sb, IParameterSymbol p, int index) {
		foreach (AttributeData attr in p.GetAttributes())
			if (renderForwardedAttribute(attr) is string rendered)
				sb.Append('[').Append(rendered).Append("] ");
		if (p.IsParams)
			sb.Append("params ");
		else if (p.ScopedKind == ScopedKind.ScopedValue || (p.ScopedKind == ScopedKind.ScopedRef && p.RefKind != RefKind.Out))
			sb.Append("scoped ");
		sb.Append(p.RefKind switch {
			RefKind.Ref => "ref ",
			RefKind.Out => "out ",
			RefKind.In => "in ",
			RefKind.RefReadOnlyParameter => "ref readonly ",
			_ => "",
		});
		sb.Append(p.Type.ToDisplayString(typeFormat)).Append(' ').Append(parameterName(p, index));
		if (p.HasExplicitDefaultValue)
			sb.Append(" = ").Append(renderDefaultValue(p.ExplicitDefaultValue, p.Type));
	}

	private static string parameterName(IParameterSymbol p, int index) =>
		p.Name.Length != 0 ? Util.EscapeIdentifier(p.Name) : "arg" + index.ToString(CultureInfo.InvariantCulture);

	private static void appendConstraints(StringBuilder sb, ITypeParameterSymbol tp) {
		List<string> constraints = new();
		if (tp.HasReferenceTypeConstraint)
			constraints.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
		else if (tp.HasUnmanagedTypeConstraint)
			constraints.Add("unmanaged");
		else if (tp.HasValueTypeConstraint)
			constraints.Add("struct");
		else if (tp.HasNotNullConstraint)
			constraints.Add("notnull");
		for (int i = 0; i < tp.ConstraintTypes.Length; i++)
			constraints.Add(tp.ConstraintTypes[i].WithNullableAnnotation(tp.ConstraintNullableAnnotations[i]).ToDisplayString(typeFormat));
		if (tp.HasConstructorConstraint && !tp.HasValueTypeConstraint)
			constraints.Add("new()");
		if (tp.AllowsRefLikeType)
			constraints.Add("allows ref struct");
		if (constraints.Count != 0)
			sb.Append(" where ").Append(Util.EscapeIdentifier(tp.Name)).Append(" : ").Append(string.Join(", ", constraints));
	}

	private static bool needsUnsafe(IMethodSymbol m) => needsUnsafe(m.ReturnType) || m.Parameters.Any(static p => needsUnsafe(p.Type));

	private static bool needsUnsafe(ITypeSymbol type) => type switch {
		IPointerTypeSymbol or IFunctionPointerTypeSymbol => true,
		IArrayTypeSymbol a => needsUnsafe(a.ElementType),
		_ => false,
	};

	// ==========================================================================
	// attributes and constants
	private static void appendAttributeLines(StringBuilder sb, ImmutableArray<AttributeData> attrs, string? target) {
		foreach (AttributeData attr in attrs)
			if (renderForwardedAttribute(attr) is string rendered)
				sb.Append("\t\t[").Append(target).Append(rendered).AppendLine("]");
	}

	private static string? renderForwardedAttribute(AttributeData attr) {
		if (attr.AttributeClass is not INamedTypeSymbol cls || !forwardedAttributes.Contains(cls.ToDisplayString()))
			return null;
		if (attr.ConstructorArguments.Any(static a => a.Kind == TypedConstantKind.Error) ||
			attr.NamedArguments.Any(static a => a.Value.Kind == TypedConstantKind.Error))
			return null;
		IEnumerable<string> args = attr.ConstructorArguments.Select(renderTypedConstant)
			.Concat(attr.NamedArguments.Select(static kv => kv.Key + " = " + renderTypedConstant(kv.Value)));
		return cls.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "(" + string.Join(", ", args) + ")";
	}

	private static string renderTypedConstant(TypedConstant c) {
		if (c.IsNull)
			return "null";
		return c.Kind switch {
			TypedConstantKind.Array => "new " + c.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " { " + string.Join(", ", c.Values.Select(renderTypedConstant)) + " }",
			TypedConstantKind.Type => "typeof(" + ((ITypeSymbol)c.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
			TypedConstantKind.Enum => "(" + c.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + renderLiteral(c.Value!) + ")",
			_ => renderLiteral(c.Value!),
		};
	}

	private static string renderDefaultValue(object? value, ITypeSymbol type) {
		if (value is null)
			return type.IsReferenceType || type is IPointerTypeSymbol ? "null" : "default";
		ITypeSymbol underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
			? nullable.TypeArguments[0]
			: type;
		if (underlying.TypeKind == TypeKind.Enum)
			return "(" + underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + renderLiteral(value) + ")";
		return renderLiteral(value);
	}

	// casts/suffixes keep the literal's type exact, which matters when the target is object
	private static string renderLiteral(object value) {
		CultureInfo inv = CultureInfo.InvariantCulture;
		return value switch {
			bool b => b ? "true" : "false",
			string s => SymbolDisplay.FormatLiteral(s, quote: true),
			char c => SymbolDisplay.FormatLiteral(c, quote: true),
			sbyte x => "(sbyte)(" + x.ToString(inv) + ")",
			byte x => "(byte)(" + x.ToString(inv) + ")",
			short x => "(short)(" + x.ToString(inv) + ")",
			ushort x => "(ushort)(" + x.ToString(inv) + ")",
			int x => x.ToString(inv),
			uint x => x.ToString(inv) + "U",
			long x => x.ToString(inv) + "L",
			ulong x => x.ToString(inv) + "UL",
			float x when float.IsNaN(x) => "global::System.Single.NaN",
			float x when float.IsPositiveInfinity(x) => "global::System.Single.PositiveInfinity",
			float x when float.IsNegativeInfinity(x) => "global::System.Single.NegativeInfinity",
			float x => x.ToString("R", inv) + "F",
			double x when double.IsNaN(x) => "global::System.Double.NaN",
			double x when double.IsPositiveInfinity(x) => "global::System.Double.PositiveInfinity",
			double x when double.IsNegativeInfinity(x) => "global::System.Double.NegativeInfinity",
			double x => x.ToString("R", inv) + "D",
			decimal x => x.ToString(inv) + "M",
			_ => throw new NotSupportedException($"unsupported constant type '{value.GetType()}'"),
		};
	}
}
