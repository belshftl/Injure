// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Input;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct EdgeType {
	public enum Case {
		Press = 1,
		Release,
	}
}
