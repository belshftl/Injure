// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using System.Text;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal static class IlReferenceDisplay {
	public static string FormatMethod(IlMethodRef method) {
		ArgumentNullException.ThrowIfNull(method);
		string name = method.Name;
		if (!method.GenericArguments.IsDefaultOrEmpty)
			name += "<" + string.Join(", ", method.GenericArguments.Select(FormatType)) + ">";
		else if (method.Signature.GenericParameterCount != 0)
			name += "<" + string.Join(", ", Enumerable.Range(0, method.Signature.GenericParameterCount).Select(static i => $"!!{i}")) + ">";
		return FormatType(method.DeclaringType) + "::" + name + "(" + string.Join(", ", method.Signature.ParameterTypes.Select(FormatType)) + ")";
	}

	public static string FormatField(IlFieldRef field) {
		ArgumentNullException.ThrowIfNull(field);
		return FormatType(field.DeclaringType) + "::" + field.Name;
	}

	public static string FormatSignature(IlMethodSignature signature) {
		ArgumentNullException.ThrowIfNull(signature);
		return FormatType(signature.ReturnType) + " (" + string.Join(", ", signature.ParameterTypes.Select(FormatType)) + ")";
	}

	public static string FormatType(IlTypeRef type) {
		ArgumentNullException.ThrowIfNull(type);
		return type switch {
			IlPrimitiveTypeRef primitive => formatPrimitive(primitive.Code),
			IlNamedTypeRef named => formatNamedType(named),
			IlGenericParameterTypeRef parameter => parameter.Kind == IlGenericParameterKind.Type
				? $"!{parameter.Index}"
				: $"!!{parameter.Index}",
			IlGenericInstanceTypeRef generic => formatGenericInstance(generic),
			IlSzArrayTypeRef array => FormatType(array.ElementType) + "[]",
			IlArrayTypeRef array => FormatType(array.ElementType) + "[" + new string(',', Math.Max(0, array.Rank - 1)) + "]",
			IlPointerTypeRef pointer => FormatType(pointer.ElementType) + "*",
			IlByRefTypeRef byRef => FormatType(byRef.ElementType) + "&",
			IlModifiedTypeRef modified => FormatType(modified.UnmodifiedType),
			IlFunctionPointerTypeRef functionPointer => "method " + FormatSignature(functionPointer.Signature) + "*",
			IlPinnedTypeRef pinned => FormatType(pinned.ElementType),
			IlGlobalModuleTypeRef => "<Module>",
			_ => type.ToString() ?? "<unknown type>",
		};
	}

	private static string formatNamedType(IlNamedTypeRef type) {
		List<IlNamedTypeRef> chain = new();
		for (IlNamedTypeRef? curr = type; curr is not null; curr = curr.DeclaringType)
			chain.Add(curr);
		chain.Reverse();

		int paramIdx = 0;
		StringBuilder sb = new();
		for (int i = 0; i < chain.Count; i++) {
			IlNamedTypeRef curr = chain[i];
			if (i == 0 && curr.Namespace.Length != 0)
				sb.Append(curr.Namespace).Append('.');
			else if (i != 0)
				sb.Append('.');
			sb.Append(curr.Name);
			if (curr.GenericArity > 0) {
				sb.Append('<');
				for (int j = 0; j < curr.GenericArity; j++) {
					if (j != 0)
						sb.Append(", ");
					sb.Append('!').Append(paramIdx++);
				}
				sb.Append('>');
			}
		}
		return sb.ToString();
	}

	private static string formatGenericInstance(IlGenericInstanceTypeRef type) {
		List<IlNamedTypeRef> chain = new();
		for (IlNamedTypeRef? curr = type.GenericType; curr is not null; curr = curr.DeclaringType)
			chain.Add(curr);
		chain.Reverse();

		int argIdx = 0;
		StringBuilder sb = new();
		for (int i = 0; i < chain.Count; i++) {
			IlNamedTypeRef curr = chain[i];
			if (i == 0 && curr.Namespace.Length != 0)
				sb.Append(curr.Namespace).Append('.');
			else if (i != 0)
				sb.Append('.');
			sb.Append(curr.Name);
			if (curr.GenericArity > 0) {
				sb.Append('<');
				for (int j = 0; j < curr.GenericArity; j++) {
					if (j != 0)
						sb.Append(", ");
					sb.Append(FormatType(type.Arguments[argIdx++]));
				}
				sb.Append('>');
			}
		}
		return sb.ToString();
	}

	private static string formatPrimitive(PrimitiveTypeCode code) => code switch {
		PrimitiveTypeCode.Void => "void",
		PrimitiveTypeCode.Boolean => "bool",
		PrimitiveTypeCode.Char => "char",
		PrimitiveTypeCode.SByte => "int8",
		PrimitiveTypeCode.Byte => "uint8",
		PrimitiveTypeCode.Int16 => "int16",
		PrimitiveTypeCode.UInt16 => "uint16",
		PrimitiveTypeCode.Int32 => "int32",
		PrimitiveTypeCode.UInt32 => "uint32",
		PrimitiveTypeCode.Int64 => "int64",
		PrimitiveTypeCode.UInt64 => "uint64",
		PrimitiveTypeCode.Single => "float32",
		PrimitiveTypeCode.Double => "float64",
		PrimitiveTypeCode.String => "string",
		PrimitiveTypeCode.TypedReference => "typedref",
		PrimitiveTypeCode.IntPtr => "native int",
		PrimitiveTypeCode.UIntPtr => "native uint",
		PrimitiveTypeCode.Object => "object",
		_ => code.ToString(),
	};
}
