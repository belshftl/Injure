// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Injure.Weaver;

public sealed class Publicizer(Context ctx) {
	private readonly Context ctx = ctx;

	public int TypesChanged { get; private set; }
	public int MembersChanged { get; private set; }

	public void Run(ModuleDefinition module) {
		foreach (TypeDefinition type in all(module)) {
			widenType(type);

			// explicit interface implementations have dotted names that c# can't call so widening
			// them doesn't really buy anything
			HashSet<MethodDefinition> eImpls = explicitImpls(type);
			foreach (MethodDefinition m in type.Methods)
				if (!eImpls.Contains(m))
					widenMethod(m);

			foreach (FieldDefinition f in type.Fields)
				widenField(f);
		}
	}

	private static IEnumerable<TypeDefinition> all(ModuleDefinition module) {
		Stack<TypeDefinition> stack = new(module.TopLevelTypes);
		while (stack.Count > 0) {
			TypeDefinition t = stack.Pop();
			yield return t;
			foreach (TypeDefinition n in t.NestedTypes)
				stack.Push(n);
		}
	}

	private void widenType(TypeDefinition type) {
		TypeAttributes visibility = type.Attributes & TypeAttributes.VisibilityMask;
		TypeAttributes want = type.DeclaringType is null ? TypeAttributes.Public : TypeAttributes.NestedPublic;

		if (ctx.Options.Unseal && canUnseal(type))
			type.Attributes &= ~TypeAttributes.Sealed;

		if (visibility == want)
			return;

		type.Attributes = (type.Attributes & ~TypeAttributes.VisibilityMask) | want;
		TypesChanged++;

		if (ctx.Options.PublicizeMarkers != PublicizeMarkerMode.None)
			mark(type.CustomAttributes, (byte)visibility);
	}

	private static bool canUnseal(TypeDefinition type) {
		if (!type.IsClass || !type.IsSealed || type.IsAbstract || type.IsValueType)
			return false;
		string? baseName = type.BaseType?.Name?.Value;
		return baseName is not "MulticastDelegate" and not "Delegate" and not "Enum" and not "ValueType";
	}

	private void widenMethod(MethodDefinition method) {
		MethodAttributes access = method.Attributes & MethodAttributes.MemberAccessMask;
		if (access == MethodAttributes.Public)
			return;

		// widening a virtual is legal per ecma-335, only narrowing relative to the base isn't
		method.Attributes =
			(method.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Public;
		MembersChanged++;

		if (ctx.Options.PublicizeMarkers == PublicizeMarkerMode.TypesAndMembers)
			mark(method.CustomAttributes, (byte)access);
	}

	private void widenField(FieldDefinition field) {
		FieldAttributes access = field.Attributes & FieldAttributes.FieldAccessMask;
		if (access == FieldAttributes.Public)
			return;

		field.Attributes = (field.Attributes & ~FieldAttributes.FieldAccessMask) | FieldAttributes.Public;
		MembersChanged++;

		if (ctx.Options.PublicizeMarkers == PublicizeMarkerMode.TypesAndMembers)
			mark(field.CustomAttributes, (byte)access);
	}

	private void mark(IList<CustomAttribute> attrs, byte original) =>
		attrs.Add(Context.Attr(ctx.PublicizedCtor, Context.Arg(ctx.Cor.Byte, original)));

	private static HashSet<MethodDefinition> explicitImpls(TypeDefinition type) {
		HashSet<MethodDefinition> set = new();
		foreach (MethodImplementation impl in type.MethodImplementations)
			if (impl.Body is MethodDefinition body)
				set.Add(body);
		return set;
	}
}
