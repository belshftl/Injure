// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;

namespace Injure.Weaver;

public static class Refify {
	public static void Finish(ModuleDefinition module, Context ctx) {
		stripBodies(module);
		module.Assembly?.CustomAttributes.Add(Context.Attr(ctx.ReferenceAssemblyCtor));
		module.Assembly?.CustomAttributes.Add(Context.Attr(ctx.ModifPublicizedCtor));
	}

	private static void stripBodies(ModuleDefinition module) {
		foreach (TypeDefinition type in allTypes(module)) {
			foreach (MethodDefinition method in type.Methods) {
				if (method.CilMethodBody is null)
					continue;
				CilMethodBody body = new();
				body.Instructions.Add(CilOpCodes.Ldnull);
				body.Instructions.Add(CilOpCodes.Throw);
				method.CilMethodBody = body;
			}
		}
	}

	private static IEnumerable<TypeDefinition> allTypes(ModuleDefinition module) {
		Stack<TypeDefinition> stack = new(module.TopLevelTypes);
		while (stack.Count > 0) {
			TypeDefinition t = stack.Pop();
			yield return t;
			foreach (TypeDefinition n in t.NestedTypes)
				stack.Push(n);
		}
	}
}
