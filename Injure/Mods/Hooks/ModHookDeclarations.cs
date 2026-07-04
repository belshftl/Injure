// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.CodeAnalysis;
using Injure.Mods.Hooks.Il;

namespace Injure.Mods.Hooks;

[DontImplement]
public interface IModHookDeclarations<L> where L : struct, IModLifetimeIdentity {
	void DeclareHook(string targetId, MethodInfo hookMethod, in ModHookConfig config);
	void DeclareHook(MethodBase targetMethod, MethodInfo hookMethod, in ModHookConfig config);

	void DeclareIlHook(string targetId, IlManipulator<L> manipulator, in ModHookConfig config);
	void DeclareIlHook(MethodBase targetMethod, IlManipulator<L> manipulator, in ModHookConfig config);
}
