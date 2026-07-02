// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.Analyzers.Core;

// mirrors Injure.CodeAnalysis.MethodConstraints
[Flags]
internal enum MethodConstraintsMirror {
	Static = 1 << 0,
	Instance = 1 << 1,

	NonPublic = 1 << 2,
	Public = 1 << 3,

	NonGeneric = 1 << 4,
	Generic = 1 << 5,

	Parameterless = 1 << 6,
	HasParameters = 1 << 7,

	ReturnsVoid = 1 << 8,
	ReturnsNonVoid = 1 << 9,
}
