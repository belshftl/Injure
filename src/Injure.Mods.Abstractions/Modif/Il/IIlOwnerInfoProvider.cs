// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Obtains an <see cref="IlOwnerInfo"/> for a given owner.
/// </summary>
internal interface IIlOwnerInfoProvider {
	IlOwnerInfo GetOwnerInfo(string ownerId);
}
