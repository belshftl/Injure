// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Hooks.Il;

internal sealed record IlInsertionEdit(int Boundary, int Sequence, IlFragment Fragment);
// TODO: IlMutationEdit, IlRemovalEdit
