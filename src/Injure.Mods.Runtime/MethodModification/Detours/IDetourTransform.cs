// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Runtime.MethodModification.Profiler;

namespace Injure.Mods.Runtime.MethodModification.Detours;

internal interface IDetourTransform {
	/// <remarks>
	/// <paramref name="body"/> is shared with the cache, so it must be cloned in order to be edited.
	/// </remarks>
	IlMethodBody Apply(MethodIdentity method, IlMethodBody body, ImmutableArray<DetourRegistration> detours);
}
