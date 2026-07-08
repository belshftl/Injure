// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

using Mono.Cecil;

namespace Injure.Mods.Abstractions.Hooks.Il;

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
		const string orShortForm = " // or short-form equivalent";
                StringBuilder sb = new();
		int leftPad = 4;
                for (int i = 0; i < pattern.Length; i++) {
                        sb.Append(' ');
			string lineno = i.ToString(CultureInfo.InvariantCulture).PadLeft(3);
			leftPad = Math.Max(leftPad, lineno.Length + 1);
                        sb.Append(lineno);
                        sb.Append(". ");
			if (!pattern[i].Kind.MatchesEquivalentShorterForms) {
				sb.AppendLine(FormatElement(pattern[i]));
			} else {
				sb.Append(FormatElement(pattern[i]));
				sb.AppendLine(orShortForm);
			}
                }
                sb.Append("    + ");
                sb.Append(FormatProvenance(provenance));
                return sb.ToString();
        }

	public static string FormatElement(IlPatternElement element) {
		return element.Kind switch {
			IlPatternElementKind.Any => "<any instruction>",

			IlPatternElementKind.OpCode => element.OpCode.Name,

			IlPatternElementKind.LdcI4 => $"ldc.i4 {element.Int.ToString(CultureInfo.InvariantCulture)}",
			IlPatternElementKind.LdcI8 => $"ldc.i8 {element.Long.ToString(CultureInfo.InvariantCulture)}",
			IlPatternElementKind.LdcR4 => $"ldc.r4 {element.Float.ToString(CultureInfo.InvariantCulture)}",
			IlPatternElementKind.LdcR8 => $"ldc.r8 {element.Double.ToString(CultureInfo.InvariantCulture)}",

			IlPatternElementKind.Ldarg => $"ldarg {element.Int.ToString(CultureInfo.InvariantCulture)}",
			IlPatternElementKind.Ldarga => $"ldarga {element.Int.ToString(CultureInfo.InvariantCulture)}",
			IlPatternElementKind.Starg => $"starg {element.Int.ToString(CultureInfo.InvariantCulture)}",

			IlPatternElementKind.Ldloc => $"ldloc {element.Int.ToString(CultureInfo.InvariantCulture)}",
			IlPatternElementKind.Ldloca => $"ldloca {element.Int.ToString(CultureInfo.InvariantCulture)}",
			IlPatternElementKind.Stloc => $"stloc {element.Int.ToString(CultureInfo.InvariantCulture)}",

			IlPatternElementKind.CecilField => $"{element.OpCode.Name} {CecilDisplay.FormatField(element.CecilField ?? throw new InternalStateException("IlPatternElement with kind CecilField is missing its CecilField value"))}",
			IlPatternElementKind.ReflectionField => $"{element.OpCode.Name} {ReflectionDisplay.FormatField(element.ReflectionField ?? throw new InternalStateException("IlPatternElement with kind ReflectionField is missing its ReflectionField value"))}",

			IlPatternElementKind.CecilMethod => $"{element.OpCode.Name} {CecilDisplay.FormatMethod(element.CecilMethod ?? throw new InternalStateException("IlPatternElement with kind CecilMethod is missing its CecilMethod value"))}",
			IlPatternElementKind.ReflectionMethod => $"{element.OpCode.Name} {ReflectionDisplay.FormatMethod(element.ReflectionMethod ?? throw new InternalStateException("IlPatternElement with kind ReflectionMethod is missing its ReflectionMethod value"))}",

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

	private static string formatNonGenericTypeName(TypeReference t) {
		string name = stripArity(t.Name);
		if (t.DeclaringType != null)
			return FormatType(t.DeclaringType) + "." + name;
		return string.IsNullOrEmpty(t.Namespace) ? name : t.Namespace + "." + name;
	}

	private static bool tryUnwrapByref(TypeReference t, [NotNullWhen(true)] out TypeReference? elem) {
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

	private static TypeReference stripModifiers(TypeReference t) {
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

	private static bool hasModifier(TypeReference t, string modifierFullName) {
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

internal static class ReflectionDisplay {
	public static string FormatMethod(MethodBase m) {
		ArgumentNullException.ThrowIfNull(m);
		return FormatType(m.DeclaringType!) + "::" + formatMethodName(m) + "(" + string.Join(", ", m.GetParameters().Select(FormatParameter)) + ")";
	}

	private static string formatMethodName(MethodBase m) {
		string name = stripArity(m.Name);
		if (m.IsGenericMethod) {
			Type[] args = m.GetGenericArguments();
			if (args.Length != 0)
				return name + "<" + string.Join(", ", args.Select(FormatType)) + ">";
		}
		return name;
	}

	public static string FormatParameter(ParameterInfo p) {
		ArgumentNullException.ThrowIfNull(p);
		Type t = p.ParameterType;
		if (!tryUnwrapByref(t, out Type? elem))
			return FormatType(t);
		if (p.IsOut && !p.IsIn)
			return "out " + FormatType(elem);
		if (hasAttr(p, "System.Runtime.CompilerServices.RequiresLocationAttribute") || hasModifier(p, "System.Runtime.CompilerServices.RequiresLocationAttribute"))
			return "ref readonly " + FormatType(elem);
		if (
			p.IsIn ||
			hasAttr(p, "System.Runtime.CompilerServices.IsReadOnlyAttribute") ||
			hasModifier(p, "System.Runtime.CompilerServices.IsReadOnlyAttribute") ||
			hasModifier(p, "System.Runtime.InteropServices.InAttribute")
		)
			return "in " + FormatType(elem);
		return "ref " + FormatType(elem);
	}

	public static string FormatField(FieldInfo f) {
		ArgumentNullException.ThrowIfNull(f);
		return FormatType(f.DeclaringType!) + "::" + f.Name;
	}

	public static string FormatType(Type t) {
		ArgumentNullException.ThrowIfNull(t);
		if (t.IsGenericParameter)
			return t.Name;
		if (t.IsByRef)
			return FormatType(t.GetElementType()!) + "&";
		if (t.IsPointer)
			return FormatType(t.GetElementType()!) + "*";
		if (t.IsArray)
			return FormatType(t.GetElementType()!) + arraySuffix(t);
		if (t.IsGenericType)
			return formatGenericTypeName(t);
		return formatNonGenericTypeName(t);
	}

	private static string formatGenericTypeName(Type t) {
		Type def = t.IsGenericTypeDefinition ? t : t.GetGenericTypeDefinition();
		Type[] args = t.GetGenericArguments();
		int argIdx = 0;
		return formatGenericTypeName(def, args, ref argIdx);
	}

	private static string formatGenericTypeName(Type t, Type[] args, ref int argIdx) {
		string name = stripArity(t.Name);
		string ret;
		if (t.DeclaringType != null)
			ret = formatGenericTypeName(t.DeclaringType, args, ref argIdx) + "." + name;
		else
			ret = string.IsNullOrEmpty(t.Namespace) ? name : t.Namespace + "." + name;

		int arity = getArity(t.Name);
		if (arity != 0) {
			ret += "<" + string.Join(", ", args.Skip(argIdx).Take(arity).Select(FormatType)) + ">";
			argIdx += arity;
		}
		return ret;
	}

	private static string formatNonGenericTypeName(Type t) {
		string name = stripArity(t.Name);
		if (t.DeclaringType != null)
			return FormatType(t.DeclaringType) + "." + name;
		return string.IsNullOrEmpty(t.Namespace) ? name : t.Namespace + "." + name;
	}

	private static bool tryUnwrapByref(Type t, [NotNullWhen(true)] out Type? elem) {
		if (t.IsByRef) {
			elem = t.GetElementType()!;
			return true;
		}
		elem = null;
		return false;
	}

	private static bool hasModifier(ParameterInfo p, string modifierFullName) =>
		p.GetRequiredCustomModifiers().Any(t => t.FullName == modifierFullName) ||
		p.GetOptionalCustomModifiers().Any(t => t.FullName == modifierFullName);

	private static bool hasAttr(ParameterInfo p, string attrFullName) => p.GetCustomAttributesData().Any(a => a.AttributeType.FullName == attrFullName);

	private static string arraySuffix(Type arr) => arr.GetArrayRank() <= 1 ? "[]" : "[" + new string(',', arr.GetArrayRank() - 1) + "]";

	private static string stripArity(string name) {
		int bt = name.IndexOf('`');
		return bt < 0 ? name : name[..bt];
	}

	private static int getArity(string name) {
		int bt = name.IndexOf('`');
		if (bt < 0 || bt == name.Length - 1)
			return 0;
		return int.TryParse(name[(bt + 1)..], out int arity) ? arity : 0;
	}
}
