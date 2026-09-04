// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Injure.Weaver;

public sealed class Emitter(Context ctx) {
	private const TypeAttributes staticClass =
		TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit;
	private const TypeAttributes delegateClass = TypeAttributes.Sealed | TypeAttributes.AnsiClass;

	private readonly Context ctx = ctx;

	public Dictionary<uint, string> Emitted { get; } = new();

	public int Emit(List<MirrorScope> roots) {
		foreach (MirrorScope scope in roots) {
			TypeDefinition mirror = new(
				mirrorNs(scope.Source.Namespace?.Value),
				NameMangler.MirrorTypeName(scope.Source),
				staticClass | TypeAttributes.NotPublic,
				ctx.Cor.Object.ToTypeDefOrRef()
			);
			ctx.Module.TopLevelTypes.Add(mirror);
			fill(mirror, scope);
		}

		ctx.Module.Assembly?.CustomAttributes.Add(
			Context.Attr(
				ctx.ModifInjectedCtor,
				Context.Arg(ctx.Cor.String, ctx.Options.MirrorRootSegment),
				Context.Arg(ctx.Cor.String, ctx.Options.NamingModeName)
			)
		);
		return Emitted.Count;
	}

	private string mirrorNs(string? ns) =>
		string.IsNullOrEmpty(ns) ? ctx.Options.MirrorRootSegment : $"{ns}.{ctx.Options.MirrorRootSegment}";

	private void fill(TypeDefinition mirror, MirrorScope scope) {
		foreach (NameGroup group in scope.Groups)
			foreach (Target target in group.Targets)
				mirror.NestedTypes.Add(makeDelegate(target));

		foreach (MirrorScope child in scope.Children) {
			TypeDefinition nested = new(
				null,
				child.MirrorName,
				staticClass | TypeAttributes.NestedAssembly,
				ctx.Cor.Object.ToTypeDefOrRef()
			);
			mirror.NestedTypes.Add(nested);
			fill(nested, child);
		}
	}

	private TypeDefinition makeDelegate(Target target) {
		MethodDefinition source = target.Method;
		MethodSignature sourceSig = source.Signature!;

		List<TypeSignature> shape = [sourceSig.ReturnType];
		TypeSignature? self = null;
		if (!source.IsStatic) {
			if (target.SelfAsObject) {
				self = ctx.Cor.Object;
			} else {
				TypeDefinition declaring = source.DeclaringType!;
				TypeSignature declaringSig = declaringSignature(declaring);
				self = declaring.IsValueType ? declaringSig.MakeByReferenceType() : declaringSig;
				shape.Add(self);
			}
		}
		shape.AddRange(sourceSig.ParameterTypes);

		var map = GenericMap.Build(source, shape);

		TypeDefinition type = new(
			null,
			target.Name,
			delegateClass | TypeAttributes.NestedAssembly,
			ctx.MulticastDelegate
		);

		foreach (GenericParameter sourceParam in map.Sources)
			type.GenericParameters.Add(new GenericParameter(sourceParam.Name) {
				Attributes = sourceParam.Attributes,
			});

		for (int i = 0; i < map.Sources.Count; i++) {
			foreach (GenericParameterConstraint c in map.Sources[i].Constraints) {
				if (GenericMap.ConstraintSignature(c.Constraint) is not TypeSignature sig)
					continue;
				type.GenericParameters[i].Constraints.Add(new GenericParameterConstraint(map.Substitute(sig).ToTypeDefOrRef()));
			}
		}

		List<TypeSignature> parameters = new();
		List<string> names = new();
		if (self is not null) {
			parameters.Add(map.Substitute(self));
			names.Add("self");
		}
		for (int i = 0; i < sourceSig.ParameterTypes.Count; i++) {
			parameters.Add(map.Substitute(sourceSig.ParameterTypes[i]));
			names.Add(paramName(source, i, names));
		}

		MethodDefinition invoke = new(
			"Invoke",
			MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
			MethodSignature.CreateInstance(map.Substitute(sourceSig.ReturnType), parameters)
		) {
			ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
		};
		for (int i = 0; i < names.Count; i++)
			invoke.ParameterDefinitions.Add(new ParameterDefinition((ushort)(i + 1), names[i], 0));

		if (self is not null)
			annotateSelfNotNull(source, self, invoke.ParameterDefinitions[0], map);

		MethodDefinition ctor = new(
			".ctor",
			MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName,
			MethodSignature.CreateInstance(ctx.Cor.Void, [ctx.Cor.Object, ctx.Cor.IntPtr])
		) {
			ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
		};
		ctor.ParameterDefinitions.Add(new ParameterDefinition(1, "object", 0));
		ctor.ParameterDefinitions.Add(new ParameterDefinition(2, "method", 0));

		type.Methods.Add(ctor);
		type.Methods.Add(invoke);

		uint token = source.MetadataToken.ToUInt32();
		type.CustomAttributes.Add(
			Context.Attr(
				ctx.ModifTargetCtor,
				Context.Arg(ctx.Cor.Int32, unchecked((int)token))
			)
		);
		Emitted[token] = TypeNameRenderer.Canonical(source);

		NullableAnnotations.Copy(ctx, source, invoke, selfOffset: self is not null ? 1 : 0);
		return type;
	}

	private void annotateSelfNotNull(MethodDefinition source, TypeSignature selfSig, ParameterDefinition selfParam, GenericMap map) {
		int declaringArity = source.DeclaringType?.GenericParameters.Count ?? 0;
		for (int i = 0; i < declaringArity; i++)
			if (i >= map.Sources.Count || map.Sources[i] != source.DeclaringType!.GenericParameters[i])
				return; // some declaring-type parameter was probably optimized out by us, slot indices would misalign

		byte[]? slots = selfSig is CorLibTypeSignature { ElementType: ElementType.Object }
			? [1]
			: source.DeclaringType is TypeDefinition declaring
				? NullableAnnotations.Slots(declaring, map.Sources)
				: null;
		if (slots is null)
			return;

		ICustomAttributeType? ctor = slots.Length == 1 ? ctx.NullableByteCtor : ctx.NullableByteArrayCtor;
		if (ctor is null)
			return;

		CustomAttributeArgument arg;
		if (slots.Length == 1) {
			arg = new CustomAttributeArgument(ctx.Cor.Byte, slots[0]);
		} else {
			// byte[] can't implicitly convert to object[] since T[] is only covariant for T : class,
			// so it's never considered for the `params object?[] elements` overload, so it instead
			// upcasts the entire array to `object` and uses the `object? value` overload instead,
			// which the writer then rejects when it tries to cast the entry to `byte`
			arg = new CustomAttributeArgument(ctx.Cor.Byte.MakeSzArrayType());
			foreach (byte b in slots)
				arg.Elements.Add(b);
		}
		selfParam.CustomAttributes.Add(
			new CustomAttribute(ctor, new CustomAttributeSignature([arg]))
		);
	}

	private static TypeSignature declaringSignature(TypeDefinition declaring) {
		if (declaring.GenericParameters.Count == 0)
			return declaring.ToTypeSignature();

		var args = new TypeSignature[declaring.GenericParameters.Count];
		for (int i = 0; i < args.Length; i++)
			args[i] = new GenericParameterSignature(GenericParameterType.Type, i);
		return new GenericInstanceTypeSignature(declaring, declaring.IsValueType, args);
	}

	private static string paramName(MethodDefinition source, int idx, List<string> taken) {
		string name = $"arg{idx}";
		foreach (ParameterDefinition def in source.ParameterDefinitions) {
			if (def.Sequence == idx + 1 && !string.IsNullOrEmpty(def.Name)) {
				name = def.Name;
				break;
			}
		}
		while (taken.Contains(name))
			name += "_";
		return name;
	}
}
