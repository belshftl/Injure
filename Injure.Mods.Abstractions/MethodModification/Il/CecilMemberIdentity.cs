// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Mono.Cecil;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal static class CecilMemberIdentity {
	public static bool SameField(FieldReference a, FieldReference b) {
		ArgumentNullException.ThrowIfNull(a);
		ArgumentNullException.ThrowIfNull(b);

		if (ReferenceEquals(a, b))
			return true;
		if (a.Name != b.Name)
			return false;
		if (a.DeclaringType is null || b.DeclaringType is null)
			return false;

		var aCtx = CecilContext.For(a.DeclaringType);
		var bCtx = CecilContext.For(b.DeclaringType);

		return sameType(a.DeclaringType, b.DeclaringType, aCtx, bCtx) && sameType(a.FieldType, b.FieldType, aCtx, bCtx);
	}

	public static bool SameField(FieldReference a, FieldInfo b) {
		ArgumentNullException.ThrowIfNull(a);
		ArgumentNullException.ThrowIfNull(b);

		if (a.Name != b.Name)
			return false;
		if (a.DeclaringType is null || b.DeclaringType is not {} bDeclaring)
			return false;

		var aCtx = CecilContext.For(a.DeclaringType);
		var bCtx = ReflectionContext.For(bDeclaring);

		return sameType(a.DeclaringType, bDeclaring, aCtx, bCtx) && sameType(a.FieldType, b.FieldType, aCtx, bCtx);
	}

	public static bool SameMethod(MethodReference a, MethodReference b) {
		ArgumentNullException.ThrowIfNull(a);
		ArgumentNullException.ThrowIfNull(b);

		if (ReferenceEquals(a, b))
			return true;
		if (a.Name != b.Name)
			return false;
		if (a.Parameters.Count != b.Parameters.Count)
			return false;
		if (methodArity(a) != methodArity(b))
			return false;
		if (a.HasThis != b.HasThis)
			return false;
		if (a.ExplicitThis != b.ExplicitThis)
			return false;
		if (a.CallingConvention != b.CallingConvention)
			return false;
		if (a.DeclaringType is null || b.DeclaringType is null)
			return false;

		var aCtx = CecilContext.For(a.DeclaringType, a);
		var bCtx = CecilContext.For(b.DeclaringType, b);

		if (!sameType(a.DeclaringType, b.DeclaringType, aCtx, bCtx))
			return false;
		if (!sameMethodTypeParams(a, b, aCtx, bCtx))
			return false;
		if (!sameType(a.ReturnType, b.ReturnType, aCtx, bCtx))
			return false;

		for (int i = 0; i < a.Parameters.Count; i++)
			if (!sameType(a.Parameters[i].ParameterType, b.Parameters[i].ParameterType, aCtx, bCtx))
				return false;

		return true;
	}

	public static bool SameMethod(MethodReference a, MethodInfo b) {
		ArgumentNullException.ThrowIfNull(a);
		ArgumentNullException.ThrowIfNull(b);

		ParameterInfo[] bParams = b.GetParameters();

		if (a.Name != b.Name)
			return false;
		if (a.Parameters.Count != bParams.Length)
			return false;
		if (methodArity(a) != methodArity(b))
			return false;
		if (a.HasThis != !b.IsStatic)
			return false;
		if (a.DeclaringType is null || b.DeclaringType is not {} bDeclaring)
			return false;

		var aCtx = CecilContext.For(a.DeclaringType, a);
		var bCtx = ReflectionContext.For(bDeclaring, b);

		if (!sameType(a.DeclaringType, bDeclaring, aCtx, bCtx))
			return false;
		if (!sameMethodTypeParams(a, b, aCtx, bCtx))
			return false;
		if (!sameType(a.ReturnType, b.ReturnType, aCtx, bCtx))
			return false;

		for (int i = 0; i < a.Parameters.Count; i++)
			if (!sameType(a.Parameters[i].ParameterType, bParams[i].ParameterType, aCtx, bCtx))
				return false;

		return true;
	}

	private readonly struct CecilContext {
		public readonly IList<TypeReference>? TypeArgs;
		public readonly IList<TypeReference>? MethodArgs;

		private CecilContext(IList<TypeReference>? typeArgs, IList<TypeReference>? methodArgs) {
			TypeArgs = typeArgs;
			MethodArgs = methodArgs;
		}

		public static CecilContext For(TypeReference? declaringType, MethodReference? method = null) =>
			new(findTypeArgs(declaringType), method is GenericInstanceMethod gim ? gim.GenericArguments : null);

		private static Mono.Collections.Generic.Collection<TypeReference>? findTypeArgs(TypeReference? t) {
			for (; t is not null; t = t.DeclaringType)
				if (t is GenericInstanceType git)
					return git.GenericArguments;
			return null;
		}
	}

	private readonly struct ReflectionContext {
		public readonly Type[]? TypeArgs;
		public readonly Type[]? MethodArgs;

		private ReflectionContext(Type[]? typeArgs, Type[]? methodArgs) {
			TypeArgs = typeArgs;
			MethodArgs = methodArgs;
		}

		public static ReflectionContext For(Type? declaringType, MethodInfo? method = null) {
			Type[]? typeArgs = null;

			for (Type? t = declaringType; t is not null; t = t.DeclaringType)
				if (t.IsGenericType && !t.IsGenericTypeDefinition) {
					typeArgs = t.GetGenericArguments();
					break;
				}

			Type[]? methodArgs = null;
			if (method is not null && method.IsGenericMethod && !method.IsGenericMethodDefinition)
				methodArgs = method.GetGenericArguments();

			return new ReflectionContext(typeArgs, methodArgs);
		}
	}

	private static int methodArity(MethodReference m) =>
		m is GenericInstanceMethod gim ? gim.GenericArguments.Count : m.GenericParameters.Count;

	private static int methodArity(MethodInfo m) =>
		m.IsGenericMethod ? m.GetGenericArguments().Length : 0;

	private static bool sameMethodTypeParams(MethodReference a, MethodReference b, CecilContext aCtx, CecilContext bCtx) {
		var ag = a as GenericInstanceMethod;
		var bg = b as GenericInstanceMethod;

		if (ag is null || bg is null)
			return ag is null && bg is null;

		if (ag.GenericArguments.Count != bg.GenericArguments.Count)
			return false;

		for (int i = 0; i < ag.GenericArguments.Count; i++)
			if (!sameType(ag.GenericArguments[i], bg.GenericArguments[i], aCtx, bCtx))
				return false;

		return true;
	}

	private static bool sameMethodTypeParams(MethodReference a, MethodInfo b, CecilContext aCtx, ReflectionContext bCtx) {
		var ag = a as GenericInstanceMethod;

		if (!b.IsGenericMethod)
			return ag is null;

		if (b.IsGenericMethodDefinition)
			return ag is null;

		Type[] bTypeParams = b.GetGenericArguments();

		if (ag is null || ag.GenericArguments.Count != bTypeParams.Length)
			return false;

		for (int i = 0; i < ag.GenericArguments.Count; i++)
			if (!sameType(ag.GenericArguments[i], bTypeParams[i], aCtx, bCtx))
				return false;

		return true;
	}

	private static TypeReference substitute(TypeReference t, CecilContext ctx) {
		for (int n = 0; n < 16 && t is GenericParameter gp; n++) {
			IList<TypeReference>? args = gp.Type == GenericParameterType.Method ? ctx.MethodArgs : ctx.TypeArgs;
			if (args is null || (uint)gp.Position >= (uint)args.Count)
				return t;

			TypeReference r = args[gp.Position];
			if (ReferenceEquals(r, t))
				return t;

			t = r;
		}

		return t;
	}

	private static Type substitute(Type t, ReflectionContext ctx) {
		for (int n = 0; n < 16 && t.IsGenericParameter; n++) {
			Type[]? args = t.DeclaringMethod is not null ? ctx.MethodArgs : ctx.TypeArgs;
			if (args is null || (uint)t.GenericParameterPosition >= (uint)args.Length)
				return t;

			Type r = args[t.GenericParameterPosition];
			if (ReferenceEquals(r, t))
				return t;

			t = r;
		}

		return t;
	}

	private static bool sameType(TypeReference a, TypeReference b, CecilContext aCtx, CecilContext bCtx) {
		a = substitute(a, aCtx);
		b = substitute(b, bCtx);

		if (ReferenceEquals(a, b))
			return true;

		if (a is GenericParameter || b is GenericParameter)
			return a is GenericParameter agp && b is GenericParameter bgp && sameGenericParameter(agp, bgp);

		if (a is GenericInstanceType || b is GenericInstanceType) {
			if (a is not GenericInstanceType ag || b is not GenericInstanceType bg)
				return false;

			if (ag.GenericArguments.Count != bg.GenericArguments.Count)
				return false;

			if (!sameType(ag.ElementType, bg.ElementType, default, default))
				return false;

			for (int i = 0; i < ag.GenericArguments.Count; i++)
				if (!sameType(ag.GenericArguments[i], bg.GenericArguments[i], aCtx, bCtx))
					return false;

			return true;
		}

		if (a is ArrayType || b is ArrayType) {
			if (a is not ArrayType aa || b is not ArrayType ba)
				return false;

			if (aa.Rank != ba.Rank || aa.IsVector != ba.IsVector || aa.Dimensions.Count != ba.Dimensions.Count)
				return false;

			for (int i = 0; i < aa.Dimensions.Count; i++) {
				ArrayDimension ad = aa.Dimensions[i];
				ArrayDimension bd = ba.Dimensions[i];

				if (ad.IsSized != bd.IsSized || ad.LowerBound != bd.LowerBound || ad.UpperBound != bd.UpperBound)
					return false;
			}

			return sameType(aa.ElementType, ba.ElementType, aCtx, bCtx);
		}

		if (a is ByReferenceType || b is ByReferenceType)
			return a is ByReferenceType ar && b is ByReferenceType br && sameType(ar.ElementType, br.ElementType, aCtx, bCtx);

		if (a is PointerType || b is PointerType)
			return a is PointerType ap && b is PointerType bp && sameType(ap.ElementType, bp.ElementType, aCtx, bCtx);

		if (a is RequiredModifierType || b is RequiredModifierType) {
			if (a is not RequiredModifierType ar || b is not RequiredModifierType br)
				return false;
			return sameType(ar.ModifierType, br.ModifierType, aCtx, bCtx) && sameType(ar.ElementType, br.ElementType, aCtx, bCtx);
		}

		if (a is OptionalModifierType || b is OptionalModifierType) {
			if (a is not OptionalModifierType ao || b is not OptionalModifierType bo)
				return false;
			return sameType(ao.ModifierType, bo.ModifierType, aCtx, bCtx) && sameType(ao.ElementType, bo.ElementType, aCtx, bCtx);
		}

		if (a is PinnedType || b is PinnedType)
			return a is PinnedType apn && b is PinnedType bpn && sameType(apn.ElementType, bpn.ElementType, aCtx, bCtx);

		if (a is SentinelType || b is SentinelType)
			return a is SentinelType ast && b is SentinelType bst && sameType(ast.ElementType, bst.ElementType, aCtx, bCtx);

		if (a is FunctionPointerType || b is FunctionPointerType) {
			if (a is not FunctionPointerType af || b is not FunctionPointerType bf)
				return false;

			if (af.Parameters.Count != bf.Parameters.Count)
				return false;
			if (af.HasThis != bf.HasThis)
				return false;
			if (af.ExplicitThis != bf.ExplicitThis)
				return false;
			if (af.CallingConvention != bf.CallingConvention)
				return false;
			if (!sameType(af.ReturnType, bf.ReturnType, aCtx, bCtx))
				return false;

			for (int i = 0; i < af.Parameters.Count; i++)
				if (!sameType(af.Parameters[i].ParameterType, bf.Parameters[i].ParameterType, aCtx, bCtx))
					return false;

			return true;
		}

		if (a is TypeSpecification || b is TypeSpecification)
			return false;

		if (a.Name != b.Name)
			return false;

		if (a.DeclaringType is null || b.DeclaringType is null) {
			if (a.DeclaringType is not null || b.DeclaringType is not null)
				return false;

			if (a.Namespace != b.Namespace)
				return false;
		} else if (!sameType(a.DeclaringType, b.DeclaringType, aCtx, bCtx)) {
			return false;
		}

		if (a.MetadataType != b.MetadataType && !bothIntrinsic(a, b))
			return false;

		return sameScope(a, b);
	}

	private static bool sameType(TypeReference a, Type b, CecilContext aCtx, ReflectionContext bCtx) {
		a = substitute(a, aCtx);
		b = substitute(b, bCtx);

		if (a is GenericParameter || b.IsGenericParameter)
			return a is GenericParameter agp && sameGenericParameter(agp, b);

		if (a is GenericInstanceType || b.IsGenericType && !b.IsGenericTypeDefinition) {
			if (a is not GenericInstanceType ag || !b.IsGenericType || b.IsGenericTypeDefinition)
				return false;

			Type[] bTypeParams = b.GetGenericArguments();
			if (ag.GenericArguments.Count != bTypeParams.Length)
				return false;

			if (!sameType(ag.ElementType, b.GetGenericTypeDefinition(), default, default))
				return false;

			for (int i = 0; i < ag.GenericArguments.Count; i++)
				if (!sameType(ag.GenericArguments[i], bTypeParams[i], aCtx, bCtx))
					return false;

			return true;
		}

		if (a is ArrayType || b.IsArray) {
			if (a is not ArrayType aa || !b.IsArray)
				return false;

			Type? be = b.GetElementType();
			if (be is null)
				return false;
			if (aa.Rank != b.GetArrayRank())
				return false;
			if (aa.IsVector != reflectionArrayIsVector(b))
				return false;

			return sameType(aa.ElementType, be, aCtx, bCtx);
		}

		if (a is ByReferenceType || b.IsByRef) {
			Type? be = b.GetElementType();
			return a is ByReferenceType ar && be is not null && sameType(ar.ElementType, be, aCtx, bCtx);
		}

		if (a is PointerType || b.IsPointer) {
			Type? be = b.GetElementType();
			return a is PointerType ap && be is not null && sameType(ap.ElementType, be, aCtx, bCtx);
		}

		if (a is RequiredModifierType rm)
			return sameType(rm.ElementType, b, aCtx, bCtx);

		if (a is OptionalModifierType om)
			return sameType(om.ElementType, b, aCtx, bCtx);

		if (a is PinnedType pn)
			return sameType(pn.ElementType, b, aCtx, bCtx);

		if (a is SentinelType st)
			return sameType(st.ElementType, b, aCtx, bCtx);

		if (a is TypeSpecification || b.HasElementType)
			return false;

		if (a.Name != b.Name)
			return false;

		if (a.DeclaringType is null || b.DeclaringType is null) {
			if (a.DeclaringType is not null || b.DeclaringType is not null)
				return false;

			if (a.Namespace != b.Namespace)
				return false;
		} else if (!sameType(a.DeclaringType, b.DeclaringType, aCtx, bCtx)) {
			return false;
		}

		return sameScope(a, b);
	}

	private static bool reflectionArrayIsVector(Type t) {
		Type? e = t.GetElementType();
		return e is not null && t == e.MakeArrayType();
	}

	private static bool sameGenericParameter(GenericParameter a, GenericParameter b) {
		if (a.Type != b.Type || a.Position != b.Position)
			return false;
		return sameGenericOwner(a.Owner, b.Owner);
	}

	private static bool sameGenericParameter(GenericParameter a, Type b) {
		if (!b.IsGenericParameter)
			return false;

		bool aMethod = a.Type == GenericParameterType.Method;
		bool bMethod = b.DeclaringMethod is not null;

		if (aMethod != bMethod || a.Position != b.GenericParameterPosition)
			return false;

		if (aMethod)
			return a.Owner is MethodReference am && b.DeclaringMethod is MethodInfo bm && sameOpenMethod(am, bm);

		return a.Owner is TypeReference at && b.DeclaringType is {} bt && sameOpenType(at, bt);
	}

	private static bool sameGenericOwner(object? a, object? b) {
		if (a is TypeReference at && b is TypeReference bt)
			return sameOpenType(at, bt);

		if (a is MethodReference am && b is MethodReference bm)
			return sameOpenMethod(am, bm);

		return a?.GetType() == b?.GetType();
	}

	private static bool sameOpenMethod(MethodReference a, MethodReference b) {
		if (a is GenericInstanceMethod ag)
			a = ag.ElementMethod;
		if (b is GenericInstanceMethod bg)
			b = bg.ElementMethod;

		if (a.Name != b.Name)
			return false;
		if (methodArity(a) != methodArity(b))
			return false;
		if (a.DeclaringType is null || b.DeclaringType is null)
			return false;

		return sameOpenType(a.DeclaringType, b.DeclaringType);
	}

	private static bool sameOpenMethod(MethodReference a, MethodInfo b) {
		if (a is GenericInstanceMethod ag)
			a = ag.ElementMethod;
		if (b.IsGenericMethod && !b.IsGenericMethodDefinition)
			b = b.GetGenericMethodDefinition();

		if (a.Name != b.Name)
			return false;
		if (methodArity(a) != methodArity(b))
			return false;
		if (a.DeclaringType is null || b.DeclaringType is not {} bd)
			return false;

		return sameOpenType(a.DeclaringType, bd);
	}

	private static bool sameOpenType(TypeReference a, TypeReference b) {
		a = openType(a);
		b = openType(b);

		if (a.Name != b.Name)
			return false;

		if (a.DeclaringType is null || b.DeclaringType is null) {
			if (a.DeclaringType is not null || b.DeclaringType is not null)
				return false;

			if (a.Namespace != b.Namespace)
				return false;
		} else if (!sameOpenType(a.DeclaringType, b.DeclaringType)) {
			return false;
		}

		return sameScope(a, b);
	}

	private static bool sameOpenType(TypeReference a, Type b) {
		a = openType(a);
		b = openType(b);

		if (a.Name != b.Name)
			return false;

		if (a.DeclaringType is null || b.DeclaringType is null) {
			if (a.DeclaringType is not null || b.DeclaringType is not null)
				return false;

			if (a.Namespace != b.Namespace)
				return false;
		} else if (!sameOpenType(a.DeclaringType, b.DeclaringType)) {
			return false;
		}

		return sameScope(a, b);
	}

	private static TypeReference openType(TypeReference t) {
		while (t is TypeSpecification ts)
			t = ts is GenericInstanceType git ? git.ElementType : ts.ElementType;
		return t;
	}

	private static Type openType(Type t) {
		while (t.HasElementType && t.GetElementType() is {} e)
			t = e;
		if (t.IsGenericType && !t.IsGenericTypeDefinition)
			t = t.GetGenericTypeDefinition();
		return t;
	}

	private static bool sameScope(TypeReference a, TypeReference b) {
		if (bothIntrinsic(a, b))
			return true;

		AssemblyNameReference? aa = assemblyNameOf(a);
		AssemblyNameReference? ba = assemblyNameOf(b);

		if (aa is null || ba is null)
			return aa is null && ba is null;

		return sameAssembly(aa, ba);
	}

	private static bool sameScope(TypeReference a, Type b) {
		if (intrinsicMatches(a, b))
			return true;

		AssemblyNameReference? aa = assemblyNameOf(a);
		return aa is not null && sameAssembly(aa, b.Assembly.GetName());
	}

	private static AssemblyNameReference? assemblyNameOf(TypeReference t) {
		t = openType(t);
		return t.Scope switch {
			AssemblyNameReference an => an,
			ModuleDefinition md => md.Assembly?.Name,
			_ => null,
		};
	}

	private static bool sameAssembly(AssemblyNameReference a, AssemblyNameReference b) {
		if (a.Name != b.Name)
			return false;
		if (a.Version is not null && b.Version is not null && a.Version != b.Version)
			return false;
		if ((a.Culture ?? "") != (b.Culture ?? ""))
			return false;
		return samePublicKeyToken(a.PublicKeyToken, b.PublicKeyToken);
	}

	private static bool sameAssembly(AssemblyNameReference a, AssemblyName b) {
		if (a.Name != b.Name)
			return false;
		if (a.Version is not null && b.Version is not null && a.Version != b.Version)
			return false;
		if ((a.Culture ?? "") != (b.CultureName ?? ""))
			return false;
		return samePublicKeyToken(a.PublicKeyToken, b.GetPublicKeyToken());
	}

	private static bool samePublicKeyToken(byte[]? a, byte[]? b) {
		int al = a?.Length ?? 0;
		int bl = b?.Length ?? 0;
		if (al != bl)
			return false;
		for (int i = 0; i < al; i++)
			if (a![i] != b![i])
				return false;
		return true;
	}

	private static bool bothIntrinsic(TypeReference a, TypeReference b) =>
		a.MetadataType == b.MetadataType
		&& a.Namespace == "System"
		&& b.Namespace == "System"
		&& a.MetadataType is
			MetadataType.Void or
			MetadataType.Boolean or
			MetadataType.Char or
			MetadataType.SByte or
			MetadataType.Byte or
			MetadataType.Int16 or
			MetadataType.UInt16 or
			MetadataType.Int32 or
			MetadataType.UInt32 or
			MetadataType.Int64 or
			MetadataType.UInt64 or
			MetadataType.Single or
			MetadataType.Double or
			MetadataType.String or
			MetadataType.IntPtr or
			MetadataType.UIntPtr or
			MetadataType.Object or
			MetadataType.TypedByReference;

	private static bool intrinsicMatches(TypeReference a, Type b) => a.MetadataType switch {
		MetadataType.Void => b == typeof(void),
		MetadataType.Boolean => b == typeof(bool),
		MetadataType.Char => b == typeof(char),
		MetadataType.SByte => b == typeof(sbyte),
		MetadataType.Byte => b == typeof(byte),
		MetadataType.Int16 => b == typeof(short),
		MetadataType.UInt16 => b == typeof(ushort),
		MetadataType.Int32 => b == typeof(int),
		MetadataType.UInt32 => b == typeof(uint),
		MetadataType.Int64 => b == typeof(long),
		MetadataType.UInt64 => b == typeof(ulong),
		MetadataType.Single => b == typeof(float),
		MetadataType.Double => b == typeof(double),
		MetadataType.String => b == typeof(string),
		MetadataType.IntPtr => b == typeof(IntPtr),
		MetadataType.UIntPtr => b == typeof(UIntPtr),
		MetadataType.Object => b == typeof(object),
		MetadataType.TypedByReference => b == typeof(TypedReference),
		_ => false,
	};
}
