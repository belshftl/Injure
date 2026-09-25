// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Obtains an <see cref="IlOwnerCtx"/> for a given owner.
/// </summary>
internal interface IIlOwnerCtxProvider {
	IlOwnerCtx GetContext(string ownerId);
}
