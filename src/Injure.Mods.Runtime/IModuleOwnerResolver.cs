// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime;

internal interface IModuleOwnerResolver {
	bool TryGetOwner(ModuleId moduleId, [NotNullWhen(true)] out string? ownerId);
}
