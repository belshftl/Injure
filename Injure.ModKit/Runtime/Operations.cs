// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.ModKit.Runtime;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ReloadRequestKind {
	public enum Case {
		SafeBoundary = 1,
		Live,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DisableRequestKind {
	public enum Case {
		Strict = 1,
		DisableDependents,
		DisableDependentsAndReloadOptionalDependents,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct EnableRequestKind {
	public enum Case {
		Strict = 1,
		EnableRequiredDependencies,
		EnableRequiredDependenciesAndReloadOptionalDependents,
	}
}
