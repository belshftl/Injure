// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Text;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Injure.Weaver;

public static class TypeNameRenderer {
	public static string Render(TypeSignature sig) {
		StringBuilder sb = new();
		write(sb, sig);
		return sb.ToString();
	}

	private static void write(StringBuilder sb, TypeSignature sig) {
		switch (sig) {
		case CorLibTypeSignature c:
			sb.Append(keyword(c));
			break;
		case GenericParameterSignature gp:
			sb.Append(gp.ParameterType == GenericParameterType.Method ? "m" : "t");
			sb.Append(gp.Index);
			break;
		case GenericInstanceTypeSignature g:
			sb.Append(name(g.GenericType));
			foreach (TypeSignature arg in g.TypeArguments) {
				sb.Append('_');
				write(sb, arg);
			}
			break;
		case ArrayTypeSignature a:
			sb.Append("arr").Append(a.Dimensions.Count).Append('_');
			write(sb, a.BaseType);
			break;
		case SzArrayTypeSignature sz:
			sb.Append("szarr_");
			write(sb, sz.BaseType);
			break;
		case PointerTypeSignature p:
			sb.Append("ptr_");
			write(sb, p.BaseType);
			break;
		case ByReferenceTypeSignature r:
			sb.Append("ref_");
			write(sb, r.BaseType);
			break;
		case PinnedTypeSignature pin:
			write(sb, pin.BaseType);
			break;
		case CustomModifierTypeSignature mod:
			write(sb, mod.BaseType);
			break;
		case FunctionPointerTypeSignature fp:
			sb.Append("fnptr_");
			write(sb, fp.Signature.ReturnType);
			foreach (TypeSignature p2 in fp.Signature.ParameterTypes) {
				sb.Append('_');
				write(sb, p2);
			}
			break;
		case BoxedTypeSignature box:
			write(sb, box.BaseType);
			break;
		case TypeDefOrRefSignature t:
			sb.Append(name(t.Type));
			break;
		default:
			sb.Append(Sanitize(sig.Name ?? "unknown"));
			break;
		}
	}

	public static string RenderParameter(TypeSignature sig, ParameterDefinition? def) {
		if (sig is ByReferenceTypeSignature byRef) {
			string prefix =
				def is null ? "ref_"
				: def.IsOut ? "out_"
				: def.IsIn ? "in_"
				: "ref_";
			return prefix + Render(byRef.BaseType);
		}
		return Render(sig);
	}

	private static string name(ITypeDefOrRef? type) =>
		type is null ? "unknown" : Sanitize(StripArity(type.Name?.Value ?? "unknown"));

	public static string StripArity(string name) {
		int bt = name.LastIndexOf('`');
		return bt < 0 ? name : name[..bt];
	}

	public static string Sanitize(string s) {
		bool clean = true;
		foreach (char c in s) {
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_')) {
				clean = false;
				break;
			}
		}
		if (clean)
			return s;

		StringBuilder sb = new(s.Length);
		foreach (char c in s)
			sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
		return sb.ToString();
	}

	private static string keyword(CorLibTypeSignature c) =>
		c.ElementType switch {
			ElementType.Void => "void",
			ElementType.Boolean => "bool",
			ElementType.Char => "char",
			ElementType.I1 => "sbyte",
			ElementType.U1 => "byte",
			ElementType.I2 => "short",
			ElementType.U2 => "ushort",
			ElementType.I4 => "int",
			ElementType.U4 => "uint",
			ElementType.I8 => "long",
			ElementType.U8 => "ulong",
			ElementType.R4 => "float",
			ElementType.R8 => "double",
			ElementType.String => "string",
			ElementType.TypedByRef => "typedref",
			ElementType.I => "nint",
			ElementType.U => "nuint",
			ElementType.Object => "object",
			_ => Sanitize(c.Name ?? "unknown"),
		};

	public static string Canonical(MethodDefinition method) {
		StringBuilder sb = new();
		MethodSignature? sig = method.Signature;

		sb.Append('[').Append((byte)(sig?.Attributes ?? 0)).Append("] ");
		sb.Append(sig?.ReturnType.FullName ?? "?").Append(' ');
		sb.Append(method.DeclaringType?.FullName ?? "?").Append("::").Append(method.Name);
		if (method.GenericParameters.Count > 0)
			sb.Append('`').Append(method.GenericParameters.Count);
		sb.Append('(');
		if (sig is not null) {
			for (int i = 0; i < sig.ParameterTypes.Count; i++) {
				if (i > 0)
					sb.Append(',');
				sb.Append(sig.ParameterTypes[i].FullName);
			}
			foreach (TypeSignature p in sig.SentinelParameterTypes)
				sb.Append(",...,").Append(p.FullName);
		}
		sb.Append(')');
		return sb.ToString();
	}
}
