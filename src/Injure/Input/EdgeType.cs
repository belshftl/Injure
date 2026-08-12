// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Input;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct EdgeType {
	public enum Case {
		Press = 1,
		Release,
	}
}
