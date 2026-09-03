// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Injure.Weaver;

public static class NullableAnnotations {
	private const string contextAttr = "NullableContextAttribute";
	private const string valueAttr = "NullableAttribute";

	public static void Copy(Context ctx, MethodDefinition source, MethodDefinition invoke, int selfOffset) {
		if (ctx.NullableContextCtor is ICustomAttributeType ctor && effectiveContextValue(source) is byte val) {
			invoke.CustomAttributes.Add(
				new CustomAttribute(
					ctor,
					new CustomAttributeSignature([
						new CustomAttributeArgument(ctx.Cor.Byte, val),
					])
				)
			);
		}

		foreach (ParameterDefinition def in source.ParameterDefinitions) {
			// 0 is the return type, not the first parameter
			int sequence = def.Sequence == 0 ? 0 : def.Sequence + selfOffset;
			ParameterDefinition dest = findOrAdd(invoke, sequence);
			foreach (CustomAttribute attr in def.CustomAttributes) {
				if (nameOf(attr) is valueAttr or contextAttr)
					dest.CustomAttributes.Add(clone(attr));
			}
		}
	}

	public static byte[]? Slots(TypeDefinition declaring, IReadOnlyList<GenericParameter> args) {
		List<byte> slots = new();
		if (!declaring.IsValueType)
			slots.Add(1);
		foreach (GenericParameter p in args) {
			bool structConstrained =
				(p.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
			if (!structConstrained)
				slots.Add(1);
		}
		return slots.Count == 0 ? null : slots.ToArray();
	}

	private static byte? effectiveContextValue(MethodDefinition method) {
		if (read(method.CustomAttributes) is byte own)
			return own;
		for (TypeDefinition? t = method.DeclaringType; t is not null; t = t.DeclaringType)
			if (read(t.CustomAttributes) is byte outer)
				return outer;
		if (((IModuleProvider)method).ContextModule is ModuleDefinition module) {
			if (read(module.CustomAttributes) is byte m)
				return m;
			if (module.Assembly is AssemblyDefinition asm && read(asm.CustomAttributes) is byte a)
				return a;
		}
		return null;
	}

	private static byte? read(IList<CustomAttribute> attrs) {
		foreach (CustomAttribute attr in attrs) {
			if (nameOf(attr) != contextAttr)
				continue;
			if (attr.Signature?.FixedArguments is not { Count: > 0 } args)
				continue;
			if (args[0].Element is byte b)
				return b;
		}
		return null;
	}

	private static ParameterDefinition findOrAdd(MethodDefinition invoke, int sequence) {
		foreach (ParameterDefinition d in invoke.ParameterDefinitions)
			if (d.Sequence == sequence)
				return d;
		ParameterDefinition added = new((ushort)sequence, null, 0);
		invoke.ParameterDefinitions.Add(added);
		return added;
	}

	private static string? nameOf(CustomAttribute attr) => attr.Constructor?.DeclaringType?.Name?.Value;

	private static CustomAttribute clone(CustomAttribute attr) {
		CustomAttributeSignature sig = new();
		if (attr.Signature is CustomAttributeSignature s) {
			foreach (CustomAttributeArgument a in s.FixedArguments)
				sig.FixedArguments.Add(a);
			foreach (CustomAttributeNamedArgument a in s.NamedArguments)
				sig.NamedArguments.Add(a);
		}
		return new CustomAttribute(attr.Constructor, sig);
	}
}
