// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

using MonoMod.Cil;

using Injure.ModKit.Abstractions;
using Injure.ModKit.Abstractions.MonoMod;

namespace Injure.ModKit.Mods.MonoMod;

/// <summary>
/// Provides utility/convenience extensions for <see cref="IModHookDeclarations{L}"/>.
/// </summary>
public static class ModHookDeclarationExtensions {
	extension<L>(IModHookDeclarations<L> declarations) where L : struct, IModLifetimeIdentity {
		public void DeclareHook(string targetID, Delegate hookMethod, in ModHookConfig config) =>
			declarations.DeclareHook(targetID, hookMethod.Method, in config);
		public void DeclareHook(MethodBase targetMethod, Delegate hookMethod, in ModHookConfig config) =>
			declarations.DeclareHook(targetMethod, hookMethod.Method, in config);

		public void DeclareILHook(string targetID, ILContext.Manipulator manipulatorMethod, in ModHookConfig config) =>
			declarations.DeclareILHook(targetID, manipulatorMethod.Method, in config);
		public void DeclareILHook(MethodBase targetMethod, ILContext.Manipulator manipulatorMethod, in ModHookConfig config) =>
			declarations.DeclareILHook(targetMethod, manipulatorMethod.Method, in config);
	}
}
