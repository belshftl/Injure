// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Injure.Mods.Runtime;

internal interface IAssemblyOwnerResolver {
	bool TryGetOwner(Assembly asm, [NotNullWhen(true)] out string? ownerId);
}
