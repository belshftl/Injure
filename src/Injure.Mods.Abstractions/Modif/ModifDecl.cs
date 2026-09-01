// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.CodeAnalysis.Internal;
using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Abstractions.Modif;

[DontCache("detour declarations for a given phase are closed after the corresponding mod lifecycle method returns")]
[DontImplement]
public interface IDetourDecl<L> where L : struct, IModLifetimeIdentity {
	void Declare(string targetId, MethodInfo impl, in DetourConfig config);
	void Declare(MethodBase targetMethod, MethodInfo impl, in DetourConfig config);
}

[DontCache("patch declarations for a given phase are closed after the corresponding mod lifecycle method returns")]
[DontImplement]
public interface IPatchDecl<L> where L : struct, IModLifetimeIdentity {
	void Declare(string targetId, IlManipulator<L> manipulator, in PatchConfig config);
	void Declare(MethodBase targetMethod, IlManipulator<L> manipulator, in PatchConfig config);
}
