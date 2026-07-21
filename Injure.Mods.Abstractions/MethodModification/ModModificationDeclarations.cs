// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.CodeAnalysis.Internal;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Abstractions.MethodModification;

[DontCache("detour declarations for a given phase are closed after the corresponding mod lifecycle method returns")]
[DontImplement]
public interface IModDetourDeclarations<L> where L : struct, IModLifetimeIdentity {
	void Declare(string targetId, MethodInfo detourMethod, in ModDetourConfig config);
	void Declare(MethodBase targetMethod, MethodInfo detourMethod, in ModDetourConfig config);
}

[DontCache("patch declarations for a given phase are closed after the corresponding mod lifecycle method returns")]
[DontImplement]
public interface IModPatchDeclarations<L> where L : struct, IModLifetimeIdentity {
	void Declare(string targetId, IlManipulator<L> manipulator, in ModPatchConfig config);
	void Declare(MethodBase targetMethod, IlManipulator<L> manipulator, in ModPatchConfig config);
}
