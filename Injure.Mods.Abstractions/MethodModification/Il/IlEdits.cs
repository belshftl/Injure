// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal sealed record IlInsertionEdit(int Boundary, int Sequence, IlFragment Fragment);
// TODO: IlMutationEdit, IlRemovalEdit
