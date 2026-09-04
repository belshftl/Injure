// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Injure.Weaver;

public sealed class GenericMap {
	private readonly Dictionary<(bool IsMethod, int Index), int> map = new();

	public List<GenericParameter> Sources { get; } = new();

	// a GenericParamConstraint row stores a TypeDefOrRef coded index with no element type prefix so
	// the value type bit is not in metadata and can't be recovered without resolving
	//
	// a TypeSpecification row already carries its own signature
	// everything else converts with the bit hardcoded in since a constraint is either a class,
	// an interface, or another generic parameter
	public static TypeSignature? ConstraintSignature(ITypeDefOrRef? constraint) =>
		constraint switch {
			null => null,
			TypeSpecification spec => spec.Signature,
			_ => constraint.ToTypeSignature(false),
		};

	public bool TryGet(GenericParameterSignature sig, out int index) =>
		map.TryGetValue((sig.ParameterType == GenericParameterType.Method, sig.Index), out index);

	public static GenericMap Build(MethodDefinition method, IReadOnlyList<TypeSignature> referenced) {
		static void add(GenericMap m, (bool, int) key, GenericParameter p) {
			m.map[key] = m.Sources.Count;
			m.Sources.Add(p);
		}

		List<GenericParameter> typeParams = method.DeclaringType?.GenericParameters?.ToList() ?? new();
		var methodParams = method.GenericParameters.ToList();

		HashSet<(bool, int)> needed = new();
		foreach (TypeSignature sig in referenced)
			walk(sig, needed);

		for (int i = 0; i < methodParams.Count; i++)
			needed.Add((true, i));

		bool changed = true;
		while (changed) {
			changed = false;
			foreach ((bool isMethod, int index) in new List<(bool, int)>(needed)) {
				List<GenericParameter> list = isMethod ? methodParams : typeParams;
				if (index >= list.Count)
					continue;
				foreach (GenericParameterConstraint c in list[index].Constraints) {
					if (ConstraintSignature(c.Constraint) is not { } cs)
						continue;
					int before = needed.Count;
					walk(cs, needed);
					changed |= needed.Count != before;
				}
			}
		}

		GenericMap result = new();
		for (int i = 0; i < typeParams.Count; i++)
			if (needed.Contains((false, i)))
				add(result, (false, i), typeParams[i]);
		for (int i = 0; i < methodParams.Count; i++)
			if (needed.Contains((true, i)))
				add(result, (true, i), methodParams[i]);
		return result;
	}

	private static void walk(TypeSignature sig, HashSet<(bool, int)> into) {
		switch (sig) {
		case GenericParameterSignature gp:
			into.Add((gp.ParameterType == GenericParameterType.Method, gp.Index));
			break;
		case GenericInstanceTypeSignature g:
			foreach (TypeSignature a in g.TypeArguments)
				walk(a, into);
			break;
		case CustomModifierTypeSignature cm:
			walk(cm.BaseType, into);
			break;
		case FunctionPointerTypeSignature fp:
			walk(fp.Signature.ReturnType, into);
			foreach (TypeSignature p in fp.Signature.ParameterTypes)
				walk(p, into);
			break;
		case TypeSpecificationSignature ts:
			walk(ts.BaseType, into);
			break;
		}
	}

	public TypeSignature Substitute(TypeSignature sig) {
		switch (sig) {
		case GenericParameterSignature gp:
			if (!TryGet(gp, out int index))
				throw new PatchException($"generic parameter {gp.FullName} is referenced but was not selected");
			return new GenericParameterSignature(GenericParameterType.Type, index);
		case GenericInstanceTypeSignature g: {
			var args = new TypeSignature[g.TypeArguments.Count];
			for (int i = 0; i < args.Length; i++)
				args[i] = Substitute(g.TypeArguments[i]);
			return new GenericInstanceTypeSignature(g.GenericType, g.IsValueType, args);
		}
		case ArrayTypeSignature a:
			return new ArrayTypeSignature(Substitute(a.BaseType), a.Dimensions.ToArray());
		case SzArrayTypeSignature sz:
			return Substitute(sz.BaseType).MakeSzArrayType();
		case PointerTypeSignature p:
			return Substitute(p.BaseType).MakePointerType();
		case ByReferenceTypeSignature r:
			return Substitute(r.BaseType).MakeByReferenceType();
		case CustomModifierTypeSignature cm:
			return new CustomModifierTypeSignature(cm.ModifierType, cm.IsRequired, Substitute(cm.BaseType));
		case FunctionPointerTypeSignature fp: {
			var ps = new TypeSignature[fp.Signature.ParameterTypes.Count];
			for (int i = 0; i < ps.Length; i++)
				ps[i] = Substitute(fp.Signature.ParameterTypes[i]);
			MethodSignature inner = new(fp.Signature.Attributes, Substitute(fp.Signature.ReturnType), ps) {
				GenericParameterCount = fp.Signature.GenericParameterCount,
			};
			return new FunctionPointerTypeSignature(inner);
		}
		default:
			return sig;
		}
	}
}
