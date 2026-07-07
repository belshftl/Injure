// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Mono.Cecil;

namespace Injure.Mods.Hooks.Il;

internal enum IlSearchDirection {
	Forward,
	Backward,
}

internal static class IlSearchDirectionExtensions {
	extension(IlSearchDirection d) {
		public string Display() => d switch {
			IlSearchDirection.Forward => "forward",
			IlSearchDirection.Backward => "backward",
			_ => throw new InternalStateException("out of range IlSearchDirection enum value"),
		};
	}
}

internal readonly record struct IlPatternSearchInfo(
	int StartInstructionBoundary,
	IlSearchDirection Direction,
	string PatternDisplay
);

internal static class IlPatternDisplay {
	public static string FormatPattern(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) {
		StringBuilder sb = new();
		for (int i = 0; i < pattern.Length; i++) {
			sb.Append(' ');
			sb.Append(i.ToString(CultureInfo.InvariantCulture).PadLeft(3));
			sb.Append(". ");
			sb.AppendLine(FormatElement(pattern[i]));
		}
		sb.Append("    + ");
		sb.Append(FormatProvenance(provenance));
		return sb.ToString();
	}

	public static string FormatElement(IlPatternElement element) {
		return element.Kind switch {
			IlPatternElementKind.Any => "<any instruction>",
			IlPatternElementKind.OpCode => element.OpCode.Name,
			IlPatternElementKind.LdcI4 => $"ldc.i4 {element.Integer.ToString(CultureInfo.InvariantCulture)}",
			IlPatternElementKind.Call => $"call {CecilDisplay.FormatMethod(element.Method ?? throw new InternalStateException("IlPatternElement with kind Call is missing its Method value"))}",
			IlPatternElementKind.Callvirt => $"callvirt {CecilDisplay.FormatMethod(element.Method ?? throw new InternalStateException("IlPatternElement with kind Callvirt is missing its Method value"))}",
			IlPatternElementKind.Field => $"{element.OpCode.Name} {CecilDisplay.FormatField(element.Field ?? throw new InternalStateException("IlPatternElement with kind Field is missing its Field value"))}",
			IlPatternElementKind.Ldarg => $"ldarg {element.Integer}",
			IlPatternElementKind.Ldloc => $"ldloc {element.Integer}",
			IlPatternElementKind.Stloc => $"stloc {element.Integer}",
			_ => "<invalid pattern element>",
		};
	}
	
	public static string FormatProvenance(IlPatternProvenanceConstraint provenance) {
		return provenance.Kind switch {
			IlPatternProvenanceConstraint.ConstraintKind.Any => "any provenance",
			IlPatternProvenanceConstraint.ConstraintKind.AllFromOwner => $"provenance owner '{provenance.OwnerId ?? throw new InternalStateException("IlPatternProvenanceConstraint with kind AllFromOwner is missing its OwnerId value")}'",
			IlPatternProvenanceConstraint.ConstraintKind.AllUnknown => "unknown provenance",
			IlPatternProvenanceConstraint.ConstraintKind.AllUniform => "uniform known provenance",
			_ => "invalid provenance constraint",
		};
	}
}

internal static class CecilDisplay {
	public static string FormatMethod(MethodReference m) {
		ArgumentNullException.ThrowIfNull(m);
		return FormatType(m.DeclaringType) + "::" + formatMethodName(m) + "(" + string.Join(", ", m.Parameters.Select(FormatParameter)) + ")";
	}

	private static string formatMethodName(MethodReference m) {
		string name = stripArity(m.Name);
		if (m is GenericInstanceMethod gen && gen.HasGenericArguments)
			return name + "<" + string.Join(", ", gen.GenericArguments.Select(FormatType)) + ">";
		if (m.HasGenericParameters)
			return name + "<" + string.Join(", ", m.GenericParameters.Select(p => p.Name)) + ">";
		return name;
	}

	public static string FormatParameter(ParameterDefinition p) {
		ArgumentNullException.ThrowIfNull(p);
		TypeReference t = p.ParameterType;
		if (!tryUnwrapByref(t, out TypeReference? elem))
			return FormatType(t);
		elem = stripModifiers(elem);
		if (p.IsOut && !p.IsIn)
			return "out " + FormatType(elem);
		if (hasAttr(p, "System.Runtime.CompilerServices.RequiresLocationAttribute") || hasModifier(t, "System.Runtime.CompilerServices.RequiresLocationAttribute"))
			return "ref readonly " + FormatType(elem);
		if (
			p.IsIn ||
			hasAttr(p, "System.Runtime.CompilerServices.IsReadOnlyAttribute") ||
			hasModifier(t, "System.Runtime.CompilerServices.IsReadOnlyAttribute") ||
			hasModifier(t, "System.Runtime.InteropServices.InAttribute")
		)
			return "in " + FormatType(elem);
		return "ref " + FormatType(elem);
	}

	public static string FormatField(FieldReference f) {
		ArgumentNullException.ThrowIfNull(f);
		return FormatType(f.DeclaringType) + "::" + f.Name;
	}

	public static string FormatType(TypeReference t) {
		ArgumentNullException.ThrowIfNull(t);
		t = stripModifiers(t);
		if (t is GenericParameter gp)
			return gp.Name;
		if (t is ByReferenceType br)
			return FormatType(br.ElementType) + "&";
		if (t is PointerType ptr)
			return FormatType(ptr.ElementType) + "*";
		if (t is ArrayType arr)
			return FormatType(arr.ElementType) + arraySuffix(arr);
		if (t is GenericInstanceType gen)
			return formatNonGenericTypeName(gen.ElementType) + "<" + string.Join(", ", gen.GenericArguments.Select(FormatType)) + ">";
		if (t is PinnedType pinned)
			return FormatType(pinned.ElementType);
		if (t is SentinelType sentinel)
			return "... " + FormatType(sentinel.ElementType);
		string name = formatNonGenericTypeName(t);
		if (t.HasGenericParameters)
			name += "<" + string.Join(", ", t.GenericParameters.Select(p => p.Name)) + ">";
		return name;
	}

	static string formatNonGenericTypeName(TypeReference t) {
		string name = stripArity(t.Name);
		if (t.DeclaringType != null)
			return FormatType(t.DeclaringType) + "." + name;
		return string.IsNullOrEmpty(t.Namespace) ? name : t.Namespace + "." + name;
	}

	static bool tryUnwrapByref(TypeReference t, [NotNullWhen(true)] out TypeReference? elem) {
		for (;;) {
			switch (t) {
			case RequiredModifierType req:
				t = req.ElementType;
				continue;
			case OptionalModifierType opt:
				t = opt.ElementType;
				continue;
			case PinnedType pinned:
				t = pinned.ElementType;
				continue;
			case ByReferenceType br:
				elem = br.ElementType;
				return true;
			default:
				elem = null;
				return false;
			}
		}
	}

	static TypeReference stripModifiers(TypeReference t) {
		for (;;) {
			switch (t) {
			case RequiredModifierType req:
				t = req.ElementType;
				continue;
			case OptionalModifierType opt:
				t = opt.ElementType;
				continue;
			default:
				return t;
			}
		}
	}

	static bool hasModifier(TypeReference t, string modifierFullName) {
		for (;;) {
			switch (t) {
			case RequiredModifierType req:
				if (req.ModifierType.FullName == modifierFullName)
					return true;
				t = req.ElementType;
				continue;
			case OptionalModifierType opt:
				if (opt.ModifierType.FullName == modifierFullName)
					return true;
				t = opt.ElementType;
				continue;
			case ByReferenceType br:
				t = br.ElementType;
				continue;
			case PinnedType pinned:
				t = pinned.ElementType;
				continue;
			default:
				return false;
			}
		}
	}

	private static bool hasAttr(ParameterDefinition p, string attrFullName) => p.HasCustomAttributes && p.CustomAttributes.Any(a => a.AttributeType.FullName == attrFullName);

	private static string arraySuffix(ArrayType arr) => arr.Rank <= 1 ? "[]" : "[" + new string(',', arr.Rank - 1) + "]";

	private static string stripArity(string name) {
		int bt = name.IndexOf('`');
		return bt < 0 ? name : name[..bt];
	}
}
